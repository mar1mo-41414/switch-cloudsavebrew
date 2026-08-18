#pragma once
#include "config.h"
#include <switch.h>
#include <stdbool.h>

typedef enum {
    SYNC_OK,
    SYNC_UP_TO_DATE,
    SYNC_ERROR,
} SyncResult;

// uid is the account this save belongs to (ignored for device saves) —
// callers resolve it via save_discovery.h's findAccountUidsForTitle/
// listAllSystemAccounts before calling in. It's also used as-is (hex-
// encoded) as the cloud repo's account segment: saves/<title_id>/<uid>/,
// or saves/<title_id>/device/ for device saves — see account_segment() in
// save_sync.c.

// Exports the title's current save data and pushes it to the repo if it
// differs from what's already there (generation-counter LWW, same scheme
// as the PC tool's `sync push`).
SyncResult syncPush(const Config *cfg, const ConfigGame *game, AccountUid uid, char *msg, size_t msgLen);

// Checks the repo's generation against what this console last synced for
// this title/account; if newer, downloads and writes it into the real save
// data. requireConfirm callback lets the caller show a confirmation prompt
// before the (destructive) write — return false from it to abort.
typedef bool (*SyncConfirmFn)(const char *summary);
SyncResult syncPull(const Config *cfg, const ConfigGame *game, AccountUid uid, SyncConfirmFn confirm, char *msg, size_t msgLen);
