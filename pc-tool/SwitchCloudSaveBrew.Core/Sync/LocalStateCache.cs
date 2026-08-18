namespace SwitchCloudSaveBrew.Core.Sync;

// Tracks, per (title, target) pair, the SyncState this machine last
// successfully synced to/from the repo. Deliberately kept outside both the
// repo checkout and the emulator's own save directory — it's local
// bookkeeping only, not something either side of the sync should see.
public static class LocalStateCache
{
    public static string PathFor(string titleId, string accountSegment, string emulator, string os)
    {
        var baseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "switch-cloudsavebrew", "local-state");
        Directory.CreateDirectory(baseDir);
        return Path.Combine(baseDir, $"{titleId}__{accountSegment}__{emulator}__{os}.json");
    }
}
