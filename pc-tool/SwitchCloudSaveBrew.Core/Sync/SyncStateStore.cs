using System.Text.Json;

namespace SwitchCloudSaveBrew.Core.Sync;

public static class SyncStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public static SyncState? TryLoad(string path)
    {
        if (!File.Exists(path))
            return null;

        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<SyncState>(stream, JsonOptions);
    }

    public static void Save(string path, SyncState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        using var stream = File.Create(path);
        JsonSerializer.Serialize(stream, state, JsonOptions);
    }
}
