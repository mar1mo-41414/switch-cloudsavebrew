namespace SwitchCloudSaveBrew.Core.Discovery;

public interface IEmulatorSaveScanner
{
    // rootDir is the emulator's own base config/data directory (e.g.
    // Ryujinx's config folder, or Eden's AppData/XDG data folder) — not
    // the save folder itself; each scanner knows its own fixed subpath.
    IReadOnlyList<DetectedSave> Scan(string rootDir);
}
