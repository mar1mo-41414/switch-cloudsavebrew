#pragma once
#include <stdint.h>
#include <stdbool.h>
#include <stddef.h>
#include "config.h"

// Thin wrapper around Gitea's Contents API (the same REST endpoints used
// from the PC tooling side of this project), so the Switch app doesn't need
// to speak the git wire protocol at all.

typedef struct {
    char name[128];
    bool is_dir;
} GiteaEntry;

// GET a file's raw (decoded) content. On success caller must free(*outData).
bool giteaGetFile(const Config *cfg, const char *repoPath,
                   uint8_t **outData, size_t *outLen, char *err, size_t errLen);

// GET a directory's entries (non-recursive). Returns count, or -1 on error
// (including "not found", which callers should treat as "empty/no data yet").
int giteaListDir(const Config *cfg, const char *repoPath,
                  GiteaEntry *out, int maxEntries, char *err, size_t errLen);

// Create or update a file. Looks up the current sha itself when the file
// already exists (Gitea's API requires it for updates).
bool giteaPutFile(const Config *cfg, const char *repoPath,
                   const uint8_t *data, size_t len, const char *message,
                   char *err, size_t errLen);
