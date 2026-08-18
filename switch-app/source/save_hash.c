#include "save_hash.h"
#include <mbedtls/sha256.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <dirent.h>
#include <sys/stat.h>

#define MAX_HASH_FILES 256
#define PATH_BUF 0x302

static char s_paths[MAX_HASH_FILES][PATH_BUF];
static int s_pathCount;

static void collect(const char *root, const char *rel)
{
    char full[PATH_BUF];
    snprintf(full, sizeof(full), "%s/%s", root, rel);

    DIR *d = opendir(full);
    if (!d)
        return;

    struct dirent *entry;
    while ((entry = readdir(d)) != NULL)
    {
        if (strcmp(entry->d_name, ".") == 0 || strcmp(entry->d_name, "..") == 0)
            continue;

        char relPath[PATH_BUF];
        if (rel[0] == '\0')
            snprintf(relPath, sizeof(relPath), "%s", entry->d_name);
        else
            snprintf(relPath, sizeof(relPath), "%s/%s", rel, entry->d_name);

        char fullPath[PATH_BUF];
        snprintf(fullPath, sizeof(fullPath), "%s/%s", root, relPath);

        struct stat st;
        if (stat(fullPath, &st) != 0)
            continue;

        if (S_ISDIR(st.st_mode))
        {
            collect(root, relPath);
        }
        else if (s_pathCount < MAX_HASH_FILES)
        {
            snprintf(s_paths[s_pathCount], PATH_BUF, "%s", relPath);
            s_pathCount++;
        }
    }
    closedir(d);
}

static int cmp(const void *a, const void *b)
{
    return strcmp((const char *)a, (const char *)b);
}

static void hash_file(const char *path, unsigned char out[32])
{
    mbedtls_sha256_context ctx;
    mbedtls_sha256_init(&ctx);
    mbedtls_sha256_starts_ret(&ctx, 0);

    FILE *f = fopen(path, "rb");
    if (f)
    {
        unsigned char buf[64 * 1024];
        size_t n;
        while ((n = fread(buf, 1, sizeof(buf), f)) > 0)
            mbedtls_sha256_update_ret(&ctx, buf, n);
        fclose(f);
    }

    mbedtls_sha256_finish_ret(&ctx, out);
    mbedtls_sha256_free(&ctx);
}

void saveHashDirectory(const char *rootDir, char *hexOut)
{
    s_pathCount = 0;
    collect(rootDir, "");
    qsort(s_paths, s_pathCount, PATH_BUF, cmp);

    mbedtls_sha256_context ctx;
    mbedtls_sha256_init(&ctx);
    mbedtls_sha256_starts_ret(&ctx, 0);

    for (int i = 0; i < s_pathCount; i++)
    {
        mbedtls_sha256_update_ret(&ctx, (const unsigned char *)s_paths[i], strlen(s_paths[i]));

        char full[PATH_BUF];
        snprintf(full, sizeof(full), "%s/%s", rootDir, s_paths[i]);
        unsigned char fileHash[32];
        hash_file(full, fileHash);
        mbedtls_sha256_update_ret(&ctx, fileHash, sizeof(fileHash));
    }

    unsigned char digest[32];
    mbedtls_sha256_finish_ret(&ctx, digest);
    mbedtls_sha256_free(&ctx);

    for (int i = 0; i < 32; i++)
        snprintf(hexOut + i * 2, 3, "%02x", digest[i]);
}
