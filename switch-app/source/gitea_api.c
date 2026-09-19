#include "gitea_api.h"
#include "l10n.h"
#include <curl/curl.h>
#include <json-c/json.h>
#include <mbedtls/base64.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#define CACERT_PATH "sdmc:/switch/switch-cloudsavebrew/cacert.pem"

typedef struct {
    char *data;
    size_t len;
} Buffer;

static size_t write_cb(void *ptr, size_t size, size_t nmemb, void *userdata)
{
    Buffer *buf = (Buffer *)userdata;
    size_t add = size * nmemb;
    char *newData = realloc(buf->data, buf->len + add + 1);
    if (!newData)
        return 0;
    buf->data = newData;
    memcpy(buf->data + buf->len, ptr, add);
    buf->len += add;
    buf->data[buf->len] = '\0';
    return add;
}

// Builds https://<api_base>/repos/<owner>/<repo>/contents/<repoPath>, with
// each path segment percent-encoded individually (so '/' stays a separator).
static void build_contents_url(CURL *curl, const Config *cfg, const char *repoPath, char *out, size_t outLen)
{
    char encoded[1024] = "";
    char pathCopy[512];
    snprintf(pathCopy, sizeof(pathCopy), "%s", repoPath);

    char *saveptr = NULL;
    char *seg = strtok_r(pathCopy, "/", &saveptr);
    bool first = true;
    while (seg)
    {
        char *esc = curl_easy_escape(curl, seg, 0);
        if (esc)
        {
            if (!first)
                strncat(encoded, "/", sizeof(encoded) - strlen(encoded) - 1);
            strncat(encoded, esc, sizeof(encoded) - strlen(encoded) - 1);
            curl_free(esc);
            first = false;
        }
        seg = strtok_r(NULL, "/", &saveptr);
    }

    snprintf(out, outLen, "%s/repos/%s/%s/contents/%s", cfg->api_base, cfg->owner, cfg->repo, encoded);
}

static CURL *make_request(const Config *cfg, struct curl_slist **headersOut)
{
    CURL *curl = curl_easy_init();
    if (!curl)
        return NULL;

    char authHeader[256];
    snprintf(authHeader, sizeof(authHeader), "Authorization: token %s", cfg->token);

    struct curl_slist *headers = NULL;
    headers = curl_slist_append(headers, authHeader);
    headers = curl_slist_append(headers, "Content-Type: application/json");
    headers = curl_slist_append(headers, "Accept: application/json");
    curl_easy_setopt(curl, CURLOPT_HTTPHEADER, headers);
    curl_easy_setopt(curl, CURLOPT_CAINFO, CACERT_PATH);
    curl_easy_setopt(curl, CURLOPT_USERAGENT, "switch-cloudsavebrew/0.1");

    // Save files can be tens of MB (e.g. Animal Crossing's main.dat is
    // ~10MB, ~13MB once base64-encoded for the JSON body) over the
    // console's Wi-Fi, and Gitea's contents API does a full git commit
    // (blob write + zlib compression + ref update) per file server-side —
    // for a file this size that can take a while with *zero* bytes moving
    // in either direction while the client just waits for the response.
    // A LOW_SPEED_LIMIT/TIME check was tried here first and made things
    // worse: it measures silence in both directions, so it fired during
    // exactly that server-processing gap even though the connection was
    // fine. Stick to a single generous flat ceiling instead.
    curl_easy_setopt(curl, CURLOPT_TIMEOUT, 600L);

    *headersOut = headers;
    return curl;
}

bool giteaGetFile(const Config *cfg, const char *repoPath,
                   uint8_t **outData, size_t *outLen, char *err, size_t errLen)
{
    struct curl_slist *headers;
    CURL *curl = make_request(cfg, &headers);
    if (!curl)
    {
        if (err) snprintf(err, errLen, "%s", L("curl_easy_init failed", "curl_easy_init失敗"));
        return false;
    }

    char url[1024];
    build_contents_url(curl, cfg, repoPath, url, sizeof(url));

    Buffer buf = {0};
    curl_easy_setopt(curl, CURLOPT_URL, url);
    curl_easy_setopt(curl, CURLOPT_WRITEFUNCTION, write_cb);
    curl_easy_setopt(curl, CURLOPT_WRITEDATA, &buf);

    CURLcode res = curl_easy_perform(curl);
    long httpCode = 0;
    curl_easy_getinfo(curl, CURLINFO_RESPONSE_CODE, &httpCode);
    curl_slist_free_all(headers);
    curl_easy_cleanup(curl);

    if (res != CURLE_OK)
    {
        if (err) snprintf(err, errLen, L("curl error: %s", "curlエラー: %s"), curl_easy_strerror(res));
        free(buf.data);
        return false;
    }
    if (httpCode != 200)
    {
        if (err) snprintf(err, errLen, L("GET %s: HTTP %ld", "GET %s: HTTP %ld (エラー)"), repoPath, httpCode);
        free(buf.data);
        return false;
    }

    json_object *root = json_tokener_parse(buf.data);
    free(buf.data);
    if (!root)
    {
        if (err) snprintf(err, errLen, L("GET %s: bad JSON", "GET %s: JSON不正"), repoPath);
        return false;
    }

    json_object *contentObj;
    if (!json_object_object_get_ex(root, "content", &contentObj))
    {
        if (err) snprintf(err, errLen, L("GET %s: no content field", "GET %s: contentフィールドが無い"), repoPath);
        json_object_put(root);
        return false;
    }

    const char *b64 = json_object_get_string(contentObj);
    size_t b64Len = strlen(b64);

    // Gitea returns base64 wrapped with newlines; mbedtls handles that fine.
    uint8_t *decoded = malloc(b64Len); // decoded is always <= encoded length
    size_t decodedLen = 0;
    int rc = mbedtls_base64_decode(decoded, b64Len, &decodedLen, (const unsigned char *)b64, b64Len);
    json_object_put(root);

    if (rc != 0)
    {
        if (err) snprintf(err, errLen, L("GET %s: base64 decode failed", "GET %s: base64デコード失敗"), repoPath);
        free(decoded);
        return false;
    }

    *outData = decoded;
    *outLen = decodedLen;
    return true;
}

int giteaListDir(const Config *cfg, const char *repoPath,
                  GiteaEntry *out, int maxEntries, char *err, size_t errLen)
{
    struct curl_slist *headers;
    CURL *curl = make_request(cfg, &headers);
    if (!curl)
        return -1;

    char url[1024];
    build_contents_url(curl, cfg, repoPath, url, sizeof(url));

    Buffer buf = {0};
    curl_easy_setopt(curl, CURLOPT_URL, url);
    curl_easy_setopt(curl, CURLOPT_WRITEFUNCTION, write_cb);
    curl_easy_setopt(curl, CURLOPT_WRITEDATA, &buf);

    CURLcode res = curl_easy_perform(curl);
    long httpCode = 0;
    curl_easy_getinfo(curl, CURLINFO_RESPONSE_CODE, &httpCode);
    curl_slist_free_all(headers);
    curl_easy_cleanup(curl);

    if (res != CURLE_OK || httpCode == 404)
    {
        free(buf.data);
        return -1; // treated by callers as "nothing there yet"
    }
    if (httpCode != 200)
    {
        if (err) snprintf(err, errLen, L("GET %s: HTTP %ld", "GET %s: HTTP %ld (エラー)"), repoPath, httpCode);
        free(buf.data);
        return -1;
    }

    json_object *root = json_tokener_parse(buf.data);
    free(buf.data);
    if (!root || json_object_get_type(root) != json_type_array)
    {
        if (err) snprintf(err, errLen, L("GET %s: expected a directory listing", "GET %s: ディレクトリ一覧を期待していた"), repoPath);
        if (root) json_object_put(root);
        return -1;
    }

    int n = json_object_array_length(root);
    int count = 0;
    for (int i = 0; i < n && count < maxEntries; i++)
    {
        json_object *entry = json_object_array_get_idx(root, i);
        json_object *nameObj, *typeObj;
        if (!json_object_object_get_ex(entry, "name", &nameObj))
            continue;
        if (!json_object_object_get_ex(entry, "type", &typeObj))
            continue;

        snprintf(out[count].name, sizeof(out[count].name), "%s", json_object_get_string(nameObj));
        out[count].is_dir = strcmp(json_object_get_string(typeObj), "dir") == 0;
        count++;
    }

    json_object_put(root);
    return count;
}

// Looks up the sha of an existing file (empty string if it doesn't exist).
static bool get_sha(const Config *cfg, const char *repoPath, char *shaOut, size_t shaOutLen)
{
    shaOut[0] = '\0';

    struct curl_slist *headers;
    CURL *curl = make_request(cfg, &headers);
    if (!curl)
        return false;

    char url[1024];
    build_contents_url(curl, cfg, repoPath, url, sizeof(url));

    Buffer buf = {0};
    curl_easy_setopt(curl, CURLOPT_URL, url);
    curl_easy_setopt(curl, CURLOPT_WRITEFUNCTION, write_cb);
    curl_easy_setopt(curl, CURLOPT_WRITEDATA, &buf);

    CURLcode res = curl_easy_perform(curl);
    long httpCode = 0;
    curl_easy_getinfo(curl, CURLINFO_RESPONSE_CODE, &httpCode);
    curl_slist_free_all(headers);
    curl_easy_cleanup(curl);

    if (res == CURLE_OK && httpCode == 200)
    {
        json_object *root = json_tokener_parse(buf.data);
        if (root)
        {
            json_object *shaObj;
            if (json_object_object_get_ex(root, "sha", &shaObj))
                snprintf(shaOut, shaOutLen, "%s", json_object_get_string(shaObj));
            json_object_put(root);
        }
    }

    free(buf.data);
    return true; // absence of a sha (new file) is not an error
}

bool giteaPutFile(const Config *cfg, const char *repoPath,
                   const uint8_t *data, size_t len, const char *message,
                   char *err, size_t errLen)
{
    char sha[64];
    get_sha(cfg, repoPath, sha, sizeof(sha));

    size_t b64Cap = ((len + 2) / 3) * 4 + 4;
    unsigned char *b64 = malloc(b64Cap);
    size_t b64Len = 0;
    if (mbedtls_base64_encode(b64, b64Cap, &b64Len, data, len) != 0)
    {
        if (err) snprintf(err, errLen, L("base64 encode failed for %s", "base64エンコード失敗: %s"), repoPath);
        free(b64);
        return false;
    }

    json_object *body = json_object_new_object();
    json_object_object_add(body, "content", json_object_new_string_len((const char *)b64, b64Len));
    json_object_object_add(body, "message", json_object_new_string(message));
    if (sha[0] != '\0')
        json_object_object_add(body, "sha", json_object_new_string(sha));
    free(b64);

    const char *bodyStr = json_object_to_json_string(body);

    struct curl_slist *headers;
    CURL *curl = make_request(cfg, &headers);
    if (!curl)
    {
        json_object_put(body);
        return false;
    }

    char url[1024];
    build_contents_url(curl, cfg, repoPath, url, sizeof(url));

    // Gitea's contents API splits create (POST, no sha) and update (PUT,
    // sha required) into two different verbs — unlike GitHub, which accepts
    // PUT for both. Using PUT unconditionally works for updates but gets
    // "[SHA]: Required" on genuinely new paths.
    const char *verb = (sha[0] != '\0') ? "PUT" : "POST";

    Buffer buf = {0};
    curl_easy_setopt(curl, CURLOPT_URL, url);
    curl_easy_setopt(curl, CURLOPT_CUSTOMREQUEST, verb);
    curl_easy_setopt(curl, CURLOPT_POSTFIELDS, bodyStr);
    curl_easy_setopt(curl, CURLOPT_WRITEFUNCTION, write_cb);
    curl_easy_setopt(curl, CURLOPT_WRITEDATA, &buf);

    CURLcode res = curl_easy_perform(curl);
    long httpCode = 0;
    curl_easy_getinfo(curl, CURLINFO_RESPONSE_CODE, &httpCode);
    curl_slist_free_all(headers);
    curl_easy_cleanup(curl);
    json_object_put(body);

    bool ok = (res == CURLE_OK) && (httpCode == 200 || httpCode == 201);
    if (!ok && err)
        snprintf(err, errLen, "%s %s: HTTP %ld: %.150s", verb, repoPath, httpCode,
                 res == CURLE_OK ? (buf.data ? buf.data : "(empty response)") : curl_easy_strerror(res));

    free(buf.data);
    return ok;
}
