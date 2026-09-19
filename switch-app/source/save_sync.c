#include "save_sync.h"
#include "save_export.h"
#include "save_hash.h"
#include "gitea_api.h"
#include "l10n.h"
#include <switch.h>
#include <json-c/json.h>
#include <stdio.h>
#include <string.h>
#include <stdlib.h>
#include <time.h>
#include <sys/stat.h>
#include <dirent.h>

#define DEVICE_NAME "Switch (Homebrew)"

// Device saves have no owning account at all, so they always go in a fixed
// "device" bucket; account saves are namespaced by the real Switch account
// uid itself — the one identity actually shared across every device/
// emulator, unlike Ryujinx/Eden's own local per-profile ids (see the PC
// tool's AccountUidResolver for that side of the mapping).
static void account_segment(const ConfigGame *game, AccountUid uid, char *out, size_t outLen)
{
    if (game->is_device_save)
        snprintf(out, outLen, "device");
    else
        snprintf(out, outLen, "%016lX%016lX", uid.uid[0], uid.uid[1]);
}

static void staging_dir(const ConfigGame *game, AccountUid uid, char *out, size_t outLen)
{
    char seg[40];
    account_segment(game, uid, seg, sizeof(seg));
    snprintf(out, outLen, "sdmc:/switch/switch-cloudsavebrew/staging/%s-%s", game->title_id_hex, seg);
}

static void state_repo_path(const ConfigGame *game, AccountUid uid, char *out, size_t outLen)
{
    char seg[40];
    account_segment(game, uid, seg, sizeof(seg));
    snprintf(out, outLen, "saves/%s/%s/state.json", game->title_id_hex, seg);
}

static void data_repo_path(const ConfigGame *game, AccountUid uid, char *out, size_t outLen)
{
    char seg[40];
    account_segment(game, uid, seg, sizeof(seg));
    snprintf(out, outLen, "saves/%s/%s/data", game->title_id_hex, seg);
}

static void local_state_path(const ConfigGame *game, AccountUid uid, char *out, size_t outLen)
{
    char seg[40];
    account_segment(game, uid, seg, sizeof(seg));
    snprintf(out, outLen, "sdmc:/switch/switch-cloudsavebrew/local-state/%s-%s.txt", game->title_id_hex, seg);
}

static long read_local_known_generation(const ConfigGame *game, AccountUid uid)
{
    char path[FS_MAX_PATH];
    local_state_path(game, uid, path, sizeof(path));

    FILE *f = fopen(path, "r");
    if (!f)
        return -1;

    long gen = -1;
    fscanf(f, "%ld", &gen);
    fclose(f);
    return gen;
}

static void write_local_known_generation(const ConfigGame *game, AccountUid uid, long gen)
{
    char path[FS_MAX_PATH];
    local_state_path(game, uid, path, sizeof(path));

    mkdir("sdmc:/switch/switch-cloudsavebrew/local-state", 0777);

    FILE *f = fopen(path, "w");
    if (f)
    {
        fprintf(f, "%ld\n", gen);
        fclose(f);
    }
}

static bool fetch_remote_state(const Config *cfg, const ConfigGame *game, AccountUid uid, long *outGen, char *outHash, size_t outHashLen)
{
    char path[256];
    state_repo_path(game, uid, path, sizeof(path));

    uint8_t *data;
    size_t len;
    if (!giteaGetFile(cfg, path, &data, &len, NULL, 0))
        return false;

    char *jsonStr = malloc(len + 1);
    memcpy(jsonStr, data, len);
    jsonStr[len] = '\0';
    free(data);

    json_object *root = json_tokener_parse(jsonStr);
    free(jsonStr);
    if (!root)
        return false;

    json_object *genObj, *hashObj;
    if (json_object_object_get_ex(root, "Generation", &genObj))
        *outGen = json_object_get_int64(genObj);
    else
        *outGen = 0;

    if (outHash && json_object_object_get_ex(root, "ContentHash", &hashObj))
        snprintf(outHash, outHashLen, "%s", json_object_get_string(hashObj));

    json_object_put(root);
    return true;
}

static void iso8601_now(char *out, size_t outLen)
{
    time_t t = time(NULL);
    struct tm tmv;
    gmtime_r(&t, &tmv);
    strftime(out, outLen, "%Y-%m-%dT%H:%M:%SZ", &tmv);
}

// --- push: local staging -> repo (recursive) ---

// Same placeholder name the PC tool's DirectoryMirror uses. Git can't track
// empty directories at all (e.g. Smash Ultimate's empty "stage" folder), so
// an empty dir gets this dropped into it before upload, and it's skipped on
// download instead of being written out as a real file.
#define EMPTY_DIR_PLACEHOLDER ".scsb-empty-dir"

static bool upload_dir_recursive(const Config *cfg, const char *localDir, const char *repoDir,
                                  const char *commitMsg, char *err, size_t errLen)
{
    DIR *d = opendir(localDir);
    if (!d)
        return true; // nothing to upload is not an error

    bool sawAnyEntry = false;
    struct dirent *entry;
    while ((entry = readdir(d)) != NULL)
    {
        if (strcmp(entry->d_name, ".") == 0 || strcmp(entry->d_name, "..") == 0)
            continue;
        sawAnyEntry = true;

        char localPath[FS_MAX_PATH], repoPath[512];
        snprintf(localPath, sizeof(localPath), "%s/%s", localDir, entry->d_name);
        snprintf(repoPath, sizeof(repoPath), "%s/%s", repoDir, entry->d_name);

        struct stat st;
        if (stat(localPath, &st) != 0)
            continue;

        if (S_ISDIR(st.st_mode))
        {
            if (!upload_dir_recursive(cfg, localPath, repoPath, commitMsg, err, errLen))
            {
                closedir(d);
                return false;
            }
            continue;
        }

        FILE *f = fopen(localPath, "rb");
        if (!f)
            continue;
        fseek(f, 0, SEEK_END);
        long size = ftell(f);
        fseek(f, 0, SEEK_SET);
        uint8_t *buf = malloc(size > 0 ? size : 1);
        fread(buf, 1, size, f);
        fclose(f);

        bool ok = giteaPutFile(cfg, repoPath, buf, size, commitMsg, err, errLen);
        free(buf);
        if (!ok)
        {
            closedir(d);
            return false;
        }
    }
    closedir(d);

    if (!sawAnyEntry)
    {
        char placeholderPath[512];
        snprintf(placeholderPath, sizeof(placeholderPath), "%s/%s", repoDir, EMPTY_DIR_PLACEHOLDER);
        if (!giteaPutFile(cfg, placeholderPath, (const uint8_t *)"", 0, commitMsg, err, errLen))
            return false;
    }

    return true;
}

SyncResult syncPush(const Config *cfg, const ConfigGame *game, AccountUid uid, char *msg, size_t msgLen)
{
    char staging[FS_MAX_PATH];
    staging_dir(game, uid, staging, sizeof(staging));

    char err[256];
    if (!saveExport(game->title_id, game->is_device_save, uid, staging, err, sizeof(err)))
    {
        if (msg) snprintf(msg, msgLen, L("export failed: %s", "エクスポート失敗: %s"), err);
        return SYNC_ERROR;
    }

    char localHash[65];
    saveHashDirectory(staging, localHash);

    long remoteGen = 0;
    char remoteHash[65] = "";
    bool hadRemote = fetch_remote_state(cfg, game, uid, &remoteGen, remoteHash, sizeof(remoteHash));

    if (hadRemote && strcmp(localHash, remoteHash) == 0)
    {
        if (msg) snprintf(msg, msgLen, "%s", L("no changes, nothing to push", "変更なし、pushするものがありません"));
        return SYNC_UP_TO_DATE;
    }

    long newGen = remoteGen + 1;

    char dataPath[256];
    data_repo_path(game, uid, dataPath, sizeof(dataPath));

    char commitMsg[256];
    snprintf(commitMsg, sizeof(commitMsg), "sync: push %s gen %ld from " DEVICE_NAME, game->title_id_hex, newGen);

    if (!upload_dir_recursive(cfg, staging, dataPath, commitMsg, err, sizeof(err)))
    {
        if (msg) snprintf(msg, msgLen, L("upload failed: %s", "アップロード失敗: %s"), err);
        return SYNC_ERROR;
    }

    char now[32];
    iso8601_now(now, sizeof(now));

    json_object *state = json_object_new_object();
    json_object_object_add(state, "TitleId", json_object_new_string(game->title_id_hex));
    json_object_object_add(state, "Generation", json_object_new_int64(newGen));
    json_object_object_add(state, "UpdatedAtUtc", json_object_new_string(now));
    json_object_object_add(state, "UpdatedByDevice", json_object_new_string(DEVICE_NAME));
    json_object_object_add(state, "ContentHash", json_object_new_string(localHash));
    const char *stateStr = json_object_to_json_string(state);

    char statePath[256];
    state_repo_path(game, uid, statePath, sizeof(statePath));
    bool ok = giteaPutFile(cfg, statePath, (const uint8_t *)stateStr, strlen(stateStr), commitMsg, err, sizeof(err));
    json_object_put(state);

    if (!ok)
    {
        if (msg) snprintf(msg, msgLen, L("state.json upload failed: %s", "state.jsonのアップロード失敗: %s"), err);
        return SYNC_ERROR;
    }

    write_local_known_generation(game, uid, newGen);

    if (msg) snprintf(msg, msgLen, L("pushed gen %ld", "gen %ld をpushしました"), newGen);
    return SYNC_OK;
}

// --- pull: repo -> local staging -> real save (recursive) ---

// mkdir only creates one level at a time and fails silently (return value
// ignored) when a parent is missing — build the whole path top-down.
static void mkdir_p(const char *path)
{
    char tmp[FS_MAX_PATH];
    snprintf(tmp, sizeof(tmp), "%s", path);

    for (char *p = tmp + 1; *p; p++)
    {
        if (*p == '/')
        {
            *p = '\0';
            mkdir(tmp, 0777);
            *p = '/';
        }
    }
    mkdir(tmp, 0777);
}

static bool download_dir_recursive(const Config *cfg, const char *repoDir, const char *localDir, char *err, size_t errLen)
{
    mkdir_p(localDir);

    GiteaEntry entries[64];
    int n = giteaListDir(cfg, repoDir, entries, 64, err, errLen);
    if (n < 0)
        return false;

    for (int i = 0; i < n; i++)
    {
        char repoPath[512], localPath[FS_MAX_PATH];
        snprintf(repoPath, sizeof(repoPath), "%s/%s", repoDir, entries[i].name);
        snprintf(localPath, sizeof(localPath), "%s/%s", localDir, entries[i].name);

        if (entries[i].is_dir)
        {
            mkdir(localPath, 0777);
            if (!download_dir_recursive(cfg, repoPath, localPath, err, errLen))
                return false;
            continue;
        }

        if (strcmp(entries[i].name, EMPTY_DIR_PLACEHOLDER) == 0)
            continue; // bookkeeping only — the empty dir itself was already created above

        uint8_t *data;
        size_t len;
        if (!giteaGetFile(cfg, repoPath, &data, &len, err, errLen))
            return false;

        FILE *f = fopen(localPath, "wb");
        if (!f)
        {
            free(data);
            if (err) snprintf(err, errLen, L("fopen failed: %s", "fopen失敗: %s"), localPath);
            return false;
        }
        fwrite(data, 1, len, f);
        fclose(f);
        free(data);
    }

    return true;
}

SyncResult syncPull(const Config *cfg, const ConfigGame *game, AccountUid uid, SyncConfirmFn confirm, char *msg, size_t msgLen)
{
    long remoteGen = 0;
    if (!fetch_remote_state(cfg, game, uid, &remoteGen, NULL, 0))
    {
        if (msg) snprintf(msg, msgLen, "%s", L("no data in repo yet for this title/account", "このタイトル/アカウントのデータはまだリポジトリにありません"));
        return SYNC_ERROR;
    }

    long localKnown = read_local_known_generation(game, uid);
    if (localKnown >= remoteGen)
    {
        if (msg) snprintf(msg, msgLen, L("already up to date (gen %ld)", "既に最新です (gen %ld)"), localKnown);
        return SYNC_UP_TO_DATE;
    }

    char summary[128];
    snprintf(summary, sizeof(summary), L("Overwrite save with repo gen %ld?", "リポジトリのgen %ld でセーブを上書きしますか?"), remoteGen);
    if (confirm && !confirm(summary))
    {
        if (msg) snprintf(msg, msgLen, "%s", L("aborted by user", "ユーザーによって中断されました"));
        return SYNC_ERROR;
    }

    char staging[FS_MAX_PATH];
    staging_dir(game, uid, staging, sizeof(staging));
    mkdir(staging, 0777);

    char dataPath[256];
    data_repo_path(game, uid, dataPath, sizeof(dataPath));

    char err[256];
    if (!download_dir_recursive(cfg, dataPath, staging, err, sizeof(err)))
    {
        if (msg) snprintf(msg, msgLen, L("download failed: %s", "ダウンロード失敗: %s"), err);
        return SYNC_ERROR;
    }

    if (!saveImport(game->title_id, game->is_device_save, uid, staging, err, sizeof(err)))
    {
        if (msg) snprintf(msg, msgLen, L("import failed: %s", "インポート失敗: %s"), err);
        return SYNC_ERROR;
    }

    write_local_known_generation(game, uid, remoteGen);

    if (msg) snprintf(msg, msgLen, L("deployed gen %ld", "gen %ld を反映しました"), remoteGen);
    return SYNC_OK;
}
