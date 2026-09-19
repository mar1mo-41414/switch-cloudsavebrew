namespace SwitchCloudSaveBrew.Core.Sync;

// Counterpart to DirectoryMirror for emulators that pack a whole save into
// one opaque file (e.g. Eden) rather than exposing it as loose files. The
// repo always stores save data under saves/<title_id>/data/ for both modes —
// here that directory just ends up holding exactly one file.
public static class FileMirror
{
    public static void Mirror(string sourceFile, string destDataDir)
    {
        Directory.CreateDirectory(destDataDir);

        // Keep the data dir holding exactly this one file — if a previous
        // sync of this target used a different filename, drop it so the repo
        // doesn't accumulate stale blobs.
        foreach (var existing in Directory.EnumerateFiles(destDataDir))
            File.Delete(existing);

        File.Copy(sourceFile, Path.Combine(destDataDir, Path.GetFileName(sourceFile)), overwrite: true);
    }

    public static void Deploy(string sourceDataDir, string destFile)
    {
        var files = Directory.EnumerateFiles(sourceDataDir).ToList();
        if (files.Count != 1)
            throw new InvalidOperationException(L.Pick(
                $"expected exactly one file in {sourceDataDir} for a file-mode target, found {files.Count}",
                $"is_file設定のターゲットとして{sourceDataDir}にはファイルが1つだけあるはずですが、{files.Count}個見つかりました"));

        Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);
        File.Copy(files[0], destFile, overwrite: true);
    }
}
