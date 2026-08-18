#pragma once
#include <switch.h>
#include <stdint.h>
#include <stddef.h>

#define DISCOVERY_MAX_GAMES 128
#define ACCOUNT_PICKER_MAX 8
// Real nicknames are capped well under this (libnx's AccountProfileBase
// nickname field is 0x20), but the hex-uid fallback used when no nickname
// is available is 32 hex chars + NUL — needs more room than 0x20.
#define ACCOUNT_NICKNAME_LEN 0x28

typedef struct {
    uint64_t title_id;
    char title_id_hex[17]; // 16 hex chars + NUL, matches ConfigGame's format
    char name[256];
    // true if this title's save data is FsSaveDataType_Device (one shared
    // save per console, no AccountUid — e.g. Animal Crossing's island data)
    // rather than the more common FsSaveDataType_Account (per-profile,
    // e.g. Smash Ultimate, Rhythm Paradise). Mounting requires a different
    // API depending on which — see save_export.c.
    bool is_device_save;
} DiscoveredGame;

// Enumerates every title with account or device save data currently on
// this console (fsOpenSaveDataInfoReader, filtered to
// FsSaveDataType_Account / FsSaveDataType_Device — this deliberately
// excludes Bcat/SystemBcat/System/Cache/Temporary save data; BCAT in
// particular is server-delivered cache, not player progress, so this tool
// doesn't sync it) and resolves each title's display name via ns (falls
// back to the hex title_id if that lookup fails for some reason). Returns
// the number of games found, or -1 on a hard failure (fs/ns service errors).
// Deduplicates purely by title_id — which account(s) actually own a given
// title's save data is resolved separately, at push/pull time, by
// findAccountUidsForTitle below (not here), since the same title can have
// independent save data under more than one account.
int discoverGames(DiscoveredGame *out, int maxGames);

// Distinct accounts on this console that currently have Account-type save
// data for titleId. Don't call this for device saves — they have no
// per-account uid at all. Used to resolve which account's save a push/pull
// should act on: exactly one match means there's nothing to ask the user,
// more than one means the caller should let them pick (see main.c's
// account picker). Returns the number found, or -1 on a hard failure.
int findAccountUidsForTitle(uint64_t titleId, AccountUid *outUids, char outNicknames[][ACCOUNT_NICKNAME_LEN], int max);

// Every Nintendo account currently on this console, regardless of save
// data. Fallback for pull when a title has never been played locally under
// any account yet, so the user can still pick which account to associate
// the pulled save with. Returns the number found, or -1 on a hard failure.
int listAllSystemAccounts(AccountUid *outUids, char outNicknames[][ACCOUNT_NICKNAME_LEN], int max);
