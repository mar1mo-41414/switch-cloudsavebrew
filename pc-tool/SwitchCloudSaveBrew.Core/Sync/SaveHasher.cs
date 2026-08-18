using System.Security.Cryptography;

namespace SwitchCloudSaveBrew.Core.Sync;

public static class SaveHasher
{
    // Hashes file contents plus relative paths, in a stable (sorted) order,
    // so the result only depends on the save's actual content — not
    // filesystem enumeration order or metadata like mtimes.
    public static string HashDirectory(string rootDir)
    {
        if (!Directory.Exists(rootDir))
            return "";

        using var sha = SHA256.Create();
        var files = Directory.EnumerateFiles(rootDir, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(rootDir, f).Replace('\\', '/'))
            .OrderBy(f => f, StringComparer.Ordinal);

        using var combined = new MemoryStream();
        foreach (var relativePath in files)
        {
            var pathBytes = System.Text.Encoding.UTF8.GetBytes(relativePath);
            combined.Write(pathBytes);

            var fileHash = sha.ComputeHash(File.ReadAllBytes(Path.Combine(rootDir, relativePath)));
            combined.Write(fileHash);
        }

        return ToHexLower(SHA256.HashData(combined.ToArray()));
    }

    public static string HashFile(string path) =>
        File.Exists(path) ? ToHexLower(SHA256.HashData(File.ReadAllBytes(path))) : "";

    private static string ToHexLower(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();
}
