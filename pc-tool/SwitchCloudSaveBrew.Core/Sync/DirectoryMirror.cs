namespace SwitchCloudSaveBrew.Core.Sync;

// Makes destDir's contents exactly match sourceDir's: copies everything over
// (including directories a game hasn't put any files into yet, e.g. Smash
// Ultimate ships an empty "stage" dir), then removes anything in destDir
// that sourceDir no longer has. Needed because plain recursive copy alone
// can't express deletions across a sync.
public static class DirectoryMirror
{
    public static void Mirror(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);

        var sourceDirs = Directory.EnumerateDirectories(sourceDir, "*", SearchOption.AllDirectories)
            .Select(d => Path.GetRelativePath(sourceDir, d))
            .ToHashSet();
        var sourceFiles = Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(sourceDir, f))
            .ToHashSet();

        foreach (var relativeDir in sourceDirs)
            Directory.CreateDirectory(Path.Combine(destDir, relativeDir));

        foreach (var relativePath in sourceFiles)
        {
            var destPath = Path.Combine(destDir, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
            File.Copy(Path.Combine(sourceDir, relativePath), destPath, overwrite: true);
        }

        foreach (var existing in Directory.EnumerateFiles(destDir, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(destDir, existing);
            if (!sourceFiles.Contains(relativePath))
                File.Delete(existing);
        }

        // Deepest-first so a directory that's only empty because its child
        // was just deleted above still gets removed.
        foreach (var existingDir in Directory.EnumerateDirectories(destDir, "*", SearchOption.AllDirectories)
            .OrderByDescending(d => d.Length))
        {
            var relativePath = Path.GetRelativePath(destDir, existingDir);
            if (!sourceDirs.Contains(relativePath) && Directory.Exists(existingDir))
                Directory.Delete(existingDir, recursive: true);
        }
    }

    private const string GitKeepFileName = ".scsb-empty-dir";

    // Git can't track empty directories at all, so an empty dir Mirror() just
    // created (e.g. Smash Ultimate's "stage") would silently vanish the next
    // time this repo dir is committed. Drop a placeholder in every empty dir
    // before staging, and strip placeholders back out when deploying to a
    // real save path so the emulator/console never sees them.
    public static void AddGitPlaceholders(string dir)
    {
        foreach (var subDir in Directory.EnumerateDirectories(dir, "*", SearchOption.AllDirectories).Append(dir))
        {
            if (!Directory.EnumerateFileSystemEntries(subDir).Any())
                File.WriteAllBytes(Path.Combine(subDir, GitKeepFileName), []);
        }
    }

    public static void RemoveGitPlaceholders(string dir)
    {
        foreach (var placeholder in Directory.EnumerateFiles(dir, GitKeepFileName, SearchOption.AllDirectories))
            File.Delete(placeholder);
    }

    // Ryujinx keeps each save as a duplex pair (0/, 1/) for power-loss
    // safety. We don't parse LibHac's internal commit-id bookkeeping to
    // know which copy is authoritative, so approximate it: whichever copy
    // has the most recently modified file wins. In practice Ryujinx keeps
    // both copies in sync during normal play, so this only matters if
    // they've actually diverged.
    public static string PickDuplexSource(string saveIdDir)
    {
        var dir0 = Path.Combine(saveIdDir, "0");
        var dir1 = Path.Combine(saveIdDir, "1");
        var has0 = Directory.Exists(dir0);
        var has1 = Directory.Exists(dir1);

        if (has0 && !has1) return dir0;
        if (has1 && !has0) return dir1;
        if (!has0 && !has1)
            throw new DirectoryNotFoundException(L.Pick(
                $"no duplex copy (0/ or 1/) found under {saveIdDir}",
                $"{saveIdDir} 配下にduplexコピー(0/または1/)が見つかりません"));

        return LatestWriteTimeUtc(dir1) > LatestWriteTimeUtc(dir0) ? dir1 : dir0;
    }

    private static DateTime LatestWriteTimeUtc(string dir) =>
        Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
            .Select(File.GetLastWriteTimeUtc)
            .DefaultIfEmpty(DateTime.MinValue)
            .Max();

    // Deploys sourceDir into whichever duplex copies exist under
    // destSaveIdDir (0/ and/or 1/) so the emulator sees consistent data no
    // matter which copy it currently treats as active. If neither exists
    // yet (fresh install, game never launched there), seeds "0" — the copy
    // Ryujinx treats as primary — so the save is there on first launch.
    public static void MirrorDuplex(string sourceDir, string destSaveIdDir)
    {
        var dir0 = Path.Combine(destSaveIdDir, "0");
        var dir1 = Path.Combine(destSaveIdDir, "1");
        var has0 = Directory.Exists(dir0);
        var has1 = Directory.Exists(dir1);

        if (!has0 && !has1)
        {
            MirrorAndCleanPlaceholders(sourceDir, dir0);
            return;
        }

        if (has0) MirrorAndCleanPlaceholders(sourceDir, dir0);
        if (has1) MirrorAndCleanPlaceholders(sourceDir, dir1);
    }

    private static void MirrorAndCleanPlaceholders(string sourceDir, string destDir)
    {
        Mirror(sourceDir, destDir);
        RemoveGitPlaceholders(destDir);
    }
}
