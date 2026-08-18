namespace SwitchCloudSaveBrew.Core.Discovery;

// Eden lays saves out as nand/user/save/<space-id>/<account-uid>/<title_id>/,
// mirroring the real console's NAND path convention directly — no binary
// parsing needed, the title_id and account-uid are right there as directory
// names. Whether a given save is a single packed file (e.g. Rhythm Paradise
// Groove's game_save.bin) or a loose-file tree (e.g. Smash Ultimate's
// save_data/) is purely a property of that game's own save format, not
// something Eden picks — verified by comparing both against real JKSV
// exports. Device-type saves (no owning account) use the real console's
// reserved all-zero account-uid folder instead of a real profile uid.
public sealed class EdenSaveScanner : IEmulatorSaveScanner
{
    public IReadOnlyList<DetectedSave> Scan(string rootDir)
    {
        var saveRoot = Path.Combine(rootDir, "nand", "user", "save");
        var results = new List<DetectedSave>();

        if (!Directory.Exists(saveRoot))
            return results;

        foreach (var spaceDir in Directory.EnumerateDirectories(saveRoot))
        foreach (var accountDir in Directory.EnumerateDirectories(spaceDir))
        {
            var isDeviceSave = IsDeviceAccountFolder(accountDir);
            var localAccountUid = isDeviceSave ? null : Path.GetFileName(accountDir).ToUpperInvariant();

            foreach (var titleDir in Directory.EnumerateDirectories(accountDir))
            {
                var titleIdHex = Path.GetFileName(titleDir);
                if (titleIdHex.Length != 16 || !titleIdHex.All(Uri.IsHexDigit))
                    continue;

                var files = Directory.GetFiles(titleDir);
                var subDirs = Directory.GetDirectories(titleDir);

                if (files.Length == 1 && subDirs.Length == 0)
                {
                    results.Add(new DetectedSave
                    {
                        TitleIdHex = titleIdHex.ToUpperInvariant(),
                        LocalPath = files[0],
                        IsFile = true,
                        IsDeviceSave = isDeviceSave,
                        LocalAccountUid = localAccountUid,
                    });
                }
                else
                {
                    results.Add(new DetectedSave
                    {
                        TitleIdHex = titleIdHex.ToUpperInvariant(),
                        LocalPath = titleDir,
                        IsFile = false,
                        IsDeviceSave = isDeviceSave,
                        LocalAccountUid = localAccountUid,
                    });
                }
            }
        }

        return results;
    }

    // Exposed so SaveSyncService can resolve the local account uid for a
    // manually-configured (non-scanned) target the same way scan results
    // already do — Eden's directory layout puts it right there in the
    // path, no parsing needed. savePath is the title dir (non-file
    // targets) or the save file itself (is_file targets).
    public static string? ReadLocalAccountUid(string savePath, bool isFile)
    {
        var titleDir = isFile ? Path.GetDirectoryName(savePath) : savePath;
        var accountDir = titleDir is null ? null : Path.GetDirectoryName(titleDir);
        if (accountDir is null)
            return null;

        return IsDeviceAccountFolder(accountDir) ? null : Path.GetFileName(accountDir).ToUpperInvariant();
    }

    private static bool IsDeviceAccountFolder(string accountDir) =>
        Path.GetFileName(accountDir).All(c => c == '0');
}
