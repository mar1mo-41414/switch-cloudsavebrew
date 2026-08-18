namespace SwitchCloudSaveBrew.Core.Discovery;

public sealed class DetectedSave
{
    public required string TitleIdHex { get; init; }
    public required string LocalPath { get; init; }
    public required bool IsFile { get; init; }

    // True for Ryujinx-style duplex (0/, 1/) save-id directories — LocalPath
    // names the save-id directory itself in that case, not a copy inside it.
    public bool IsDuplex { get; init; }

    // FsSaveDataType_Device (one shared save per console, no owning
    // account) rather than the default per-profile Account type.
    public bool IsDeviceSave { get; init; }

    // The emulator's own local profile uid that owns this save (hex,
    // uppercase) — null for device saves (no owning account) or when it
    // couldn't be determined. This is NOT the real Switch account uid;
    // it's whatever id Ryujinx/Eden assigned that local profile. See
    // AccountUidResolver for how it maps to the real one via config.yaml's
    // accounts: table.
    public string? LocalAccountUid { get; init; }

    // No cheap way to recover a game's display name from either emulator's
    // on-disk save layout alone (that lives in the installed title's own
    // metadata cache, which differs per emulator and isn't stable to
    // depend on) — callers fill this in from config.yaml if a matching
    // title_id is already configured there, otherwise it's just the hex id.
    public string Name { get; set; } = "";
}
