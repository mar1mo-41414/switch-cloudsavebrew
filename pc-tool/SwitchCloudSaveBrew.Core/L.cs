namespace SwitchCloudSaveBrew.Core;

public enum Language
{
    En,
    Ja,
}

// Minimal hand-rolled localization instead of .resx/ResourceManager —
// satellite resource assemblies are extra files that complicate the
// project's self-contained single-file publish (build.sh/build.ps1), so
// this keeps every string in the main assembly instead. Core's own
// exception/result messages need to respect whichever frontend is
// currently running, so both CLI and GUI share this one static: the CLI
// sets it once from config.yaml's language: field at startup, the GUI
// sets it from its own persisted GuiSettings (and updates it live when
// the user toggles the language selector).
public static class L
{
    public static Language Current { get; set; } = Language.En;

    public static string Pick(string en, string ja) => Current == Language.Ja ? ja : en;

    public static Language Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "ja" or "jp" or "japanese" => Language.Ja,
        _ => Language.En,
    };
}
