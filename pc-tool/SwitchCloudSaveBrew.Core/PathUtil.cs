namespace SwitchCloudSaveBrew.Core;

public static class PathUtil
{
    public static string Expand(string path)
    {
        // %APPDATA%-style vars (Windows config paths) and ~/-style home dir
        // (Linux config paths) — config files may need either depending on
        // which machine/emulator a target is for.
        var expanded = Environment.ExpandEnvironmentVariables(path);
        return expanded.StartsWith('~')
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), expanded.TrimStart('~').TrimStart('/', '\\'))
            : expanded;
    }
}
