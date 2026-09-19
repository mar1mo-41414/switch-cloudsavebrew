#pragma once
#include <stdint.h>
#include <stdbool.h>
#include <stddef.h>

#define CONFIG_MAX_GAMES 64
#define CONFIG_STR_LEN 128

typedef struct {
    char title_id_hex[17]; // 16 hex chars + NUL
    uint64_t title_id;
    char name[CONFIG_STR_LEN];
    // FsSaveDataType_Device (one shared save per console, e.g. Animal
    // Crossing's island) vs the default FsSaveDataType_Account (per-profile).
    // Entries from config.ini's [games] list always default to false —
    // auto-detected entries (save_discovery.c) set this correctly from what
    // the console actually reports.
    bool is_device_save;
} ConfigGame;

typedef struct {
    char api_base[CONFIG_STR_LEN];  // e.g. https://your-gitea-instance.example/api/v1
    char owner[CONFIG_STR_LEN];     // e.g. your-username
    char repo[CONFIG_STR_LEN];      // e.g. switch-savedata
    char token[CONFIG_STR_LEN];     // Gitea access token

    ConfigGame games[CONFIG_MAX_GAMES];
    int game_count;

    // [ui] language= ("en" or "ja"). Read by main() to set l10n.h's
    // g_language once at startup; defaults to English if absent/unset.
    char language[8];
} Config;

// Loads config from SD:/switch/switch-cloudsavebrew/config.ini.
// Returns true on success. On failure *err_out (if non-NULL) is set to a
// human-readable message suitable for on-screen display.
bool configLoad(Config *cfg, char *err_out, size_t err_out_len);
