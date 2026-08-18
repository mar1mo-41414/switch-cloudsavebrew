namespace SwitchCloudSaveBrew.Core.Discovery;

// Ryujinx stores each save under bis/user/save/<numeric-save-id>/, as a
// duplex pair (0/, 1/) plus ExtraData0/ExtraData1. The save-id itself is
// just an internal index — ExtraData0 is a raw dump of LibHac's
// SaveDataExtraData struct, which has what we need: ApplicationId
// (title_id) as the first 8 bytes, SaveDataAttribute.UserId as the next 16
// bytes, and SaveDataAttribute.Type at byte offset 32 (ProgramId(8) +
// UserId(16) + StaticSaveDataId(8)). Verified against real data: Super
// Smash Bros. Ultimate has two save-id folders sharing the same title_id —
// one Type=1 (Account, the real save) and one Type=2 (Bcat, Nintendo-
// server-delivered cache) — filtering to Account/Device only (matching the
// same decision made for the Switch app's own discoverGames()) correctly
// excludes the Bcat one.
public sealed class RyujinxSaveScanner : IEmulatorSaveScanner
{
    private const byte SaveDataType_Account = 1;
    private const byte SaveDataType_Device = 3;

    public IReadOnlyList<DetectedSave> Scan(string rootDir)
    {
        var saveRoot = Path.Combine(rootDir, "bis", "user", "save");
        var results = new List<DetectedSave>();

        if (!Directory.Exists(saveRoot))
            return results;

        foreach (var saveIdDir in Directory.EnumerateDirectories(saveRoot))
        {
            var header = ReadExtraDataHeader(saveIdDir);
            if (header is null)
                continue;

            var titleId = BitConverter.ToUInt64(header, 0);
            if (titleId == 0)
                continue; // not an account/device-type save (system save, etc.)

            var saveDataType = header[32];
            if (saveDataType != SaveDataType_Account && saveDataType != SaveDataType_Device)
                continue; // e.g. Bcat — server-delivered cache, not player progress

            // Report the save-id directory itself, not either copy —
            // duplex resolution (which of 0/, 1/ is canonical, or writing
            // to both on pull) is DirectoryMirror's job, not this scanner's.
            var hasDir0 = Directory.Exists(Path.Combine(saveIdDir, "0"));
            var hasDir1 = Directory.Exists(Path.Combine(saveIdDir, "1"));
            if (!hasDir0 && !hasDir1)
                continue;

            var isDeviceSave = saveDataType == SaveDataType_Device;

            results.Add(new DetectedSave
            {
                TitleIdHex = titleId.ToString("X16"),
                LocalPath = saveIdDir,
                IsFile = false,
                IsDuplex = true,
                IsDeviceSave = isDeviceSave,
                LocalAccountUid = isDeviceSave ? null : Convert.ToHexString(header, 8, 16),
            });
        }

        return results;
    }

    // Exposed so SaveSyncService can resolve the local account uid for a
    // manually-configured (non-scanned) target the same way scan results
    // already do. Returns null for device saves (no owning account) or if
    // ExtraData0 can't be read (e.g. the save-id dir hasn't been created
    // yet — the game has never been run locally under any profile).
    public static string? ReadLocalAccountUid(string saveIdDir)
    {
        var header = ReadExtraDataHeader(saveIdDir);
        if (header is null)
            return null;

        return header[32] == SaveDataType_Account ? Convert.ToHexString(header, 8, 16) : null;
    }

    private static byte[]? ReadExtraDataHeader(string saveIdDir)
    {
        var extraDataPath = Path.Combine(saveIdDir, "ExtraData0");
        if (!File.Exists(extraDataPath))
            return null;

        try
        {
            using var f = File.OpenRead(extraDataPath);
            var header = new byte[33];
            return f.Read(header, 0, header.Length) == header.Length ? header : null;
        }
        catch (IOException)
        {
            return null;
        }
    }
}
