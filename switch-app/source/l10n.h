#pragma once

// Minimal hand-rolled localization, same approach as the PC tool's Core/L.cs
// (no gettext/locale machinery — homebrew apps don't have that available
// anyway). config.ini's [ui] language= sets this once at startup; the
// main menu's [-] key also toggles it live. Defaults to English.
typedef enum {
    LANG_EN,
    LANG_JA,
} Language;

extern Language g_language;

// Call sites read like L("English text", "日本語テキスト").
static inline const char *L(const char *en, const char *ja)
{
    return g_language == LANG_JA ? ja : en;
}
