namespace SwitchCloudSaveBrew.Core.Config;

public sealed class AppConfig
{
    public RemoteConfig Remote { get; set; } = new();
    public KeysConfig Keys { get; set; } = new();
    public List<GameConfig> Games { get; set; } = [];

    // "en" or "ja" — drives SwitchCloudSaveBrew.Core.L.Current for CLI
    // runs (set once at startup from this). The GUI has its own
    // independent toggle (GuiSettings.Language) instead of reading this,
    // so a shared config.yaml can drive CLI output in one language while
    // the GUI shows another. Defaults to English when absent.
    public string Language { get; set; } = "en";

    // Maps a real Switch account to that same person's local per-emulator
    // profile ids. The two are unrelated identifier namespaces — Ryujinx/
    // Eden each assign their own local profile uid when a user creates a
    // profile, with no relation to the real Nintendo Account uid — so this
    // mapping has to be explicit, not inferred. Used by
    // AccountUidResolver to pick which saves/<title_id>/<segment>/ bucket
    // a target's save belongs to.
    public List<AccountConfig> Accounts { get; set; } = [];
}

public sealed class AccountConfig
{
    public string Name { get; set; } = "";

    // The real Switch AccountUid (32 hex chars) — this is the value used
    // as the cloud repo's account segment, since it's the one identity
    // that's actually shared across every device. Get it from switch-app's
    // "accounts" debug screen.
    public string SwitchUid { get; set; } = "";

    public string RyujinxUid { get; set; } = "";
    public string EdenUid { get; set; } = "";
}

public sealed class RemoteConfig
{
    public string GitUrl { get; set; } = "";
    public string DeployKeyPath { get; set; } = "";

    // Where the savedata repo is kept checked out locally. Defaults to a
    // per-user cache dir if left blank.
    public string LocalCachePath { get; set; } = "~/.cache/switch-cloudsavebrew/savedata-repo";
}

public sealed class KeysConfig
{
    // prod.keys (title keys / common keys) extracted from the user's own console.
    public string ProdKeysPath { get; set; } = "";

    // SD Seed derived from the console's PRODINFO, as a hex string. Required
    // to unwrap the NAX0 container that Switch save exports are stored in on SD.
    public string SdSeed { get; set; } = "";
}

public sealed class GameConfig
{
    public string TitleId { get; set; } = "";
    public string Name { get; set; } = "";
    public List<TargetConfig> Targets { get; set; } = [];
}

public sealed class TargetConfig
{
    public string Emulator { get; set; } = "";
    public string Os { get; set; } = "";
    public string SavePath { get; set; } = "";

    // Some emulators (e.g. Eden) pack a whole save into one opaque file
    // rather than exposing loose per-file save contents in a directory
    // (Ryujinx-style). When true, save_path names that single file directly
    // and it's synced as an atomic blob — no need to understand its internal
    // format, just move the bytes.
    public bool IsFile { get; set; }

    // Ryujinx keeps each save as a duplex pair of directories (0/, 1/) under
    // save_path for power-loss safety. When true, save_path names the
    // save-id directory that contains them, not a copy directly — see
    // DirectoryMirror.PickDuplexSource/MirrorDuplex for how push/pull
    // resolve which copy (or copies) to use.
    public bool IsDuplex { get; set; }

    // FsSaveDataType_Device (one shared save per console, e.g. Animal
    // Crossing's island — no owning account at all) rather than the
    // default FsSaveDataType_Account (per-profile). Device saves always
    // sync through the fixed "device" cloud bucket — AccountUidResolver
    // skips account resolution entirely for these.
    public bool IsDeviceSave { get; set; }
}
