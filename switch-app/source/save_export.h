#pragma once
#include <switch.h>
#include <stdint.h>
#include <stdbool.h>
#include <stddef.h>

// Exports the given title's save data (mounted read-only via the official
// save-mount API, same mechanism JKSV/Checkpoint use) into stagingDir on the
// SD card, as plain files — mirroring whatever the game itself wrote.
// isDeviceSave selects fsdevMountDeviceSaveData (one shared save per
// console, e.g. Animal Crossing's island — no ReadOnly variant exists for
// this type) instead of the usual per-profile fsdevMountSaveDataReadOnly;
// when true, uid is ignored (device saves have no owning account). The
// caller (main.c) is responsible for resolving which account's save this
// should be — see save_discovery.h's findAccountUidsForTitle/
// listAllSystemAccounts and the account picker in main.c.
bool saveExport(uint64_t titleId, bool isDeviceSave, AccountUid uid, const char *stagingDir, char *err, size_t errLen);

// Mirrors stagingDir into the title's save data (mounted read-write) and
// commits. This overwrites whatever the game currently has saved —
// callers must confirm with the user before calling this.
bool saveImport(uint64_t titleId, bool isDeviceSave, AccountUid uid, const char *stagingDir, char *err, size_t errLen);
