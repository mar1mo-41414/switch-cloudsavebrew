using System.Diagnostics;
using SwitchCloudSaveBrew.Core.Config;

namespace SwitchCloudSaveBrew.Core.Sync;

// Thin wrapper around the system `git` binary. Deliberately not a git
// library (e.g. LibGit2Sharp) — the deploy key / SSH config this project
// relies on is exactly what the user's ambient `git`/`ssh` setup already
// knows how to use, so shelling out avoids reimplementing that.
public sealed class RepoSync(RemoteConfig remote)
{
    public string LocalPath { get; } = PathUtil.Expand(remote.LocalCachePath);

    public void EnsureUpToDate()
    {
        if (Directory.Exists(Path.Combine(LocalPath, ".git")))
        {
            Run(LocalPath, "fetch", "origin");
            Run(LocalPath, "reset", "--hard", "origin/HEAD");
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LocalPath) ?? ".");
            Run(workingDir: null, "clone", remote.GitUrl, LocalPath);

            // This is a disposable local cache clone, not something the user
            // interacts with directly — commit signing needs an interactive
            // pinentry this process doesn't have a terminal for, so skip it
            // here the same way we did for the main project repo.
            Run(LocalPath, "config", "commit.gpgsign", "false");
        }
    }

    public bool CommitAndPush(string message)
    {
        Run(LocalPath, "add", "-A");

        // Nothing staged is a legitimate no-op — check for it explicitly
        // rather than inferring it from `git commit`'s exit code, since a
        // real failure (e.g. signing) also exits non-zero and must not be
        // swallowed as "nothing to commit".
        var (diffExitCode, _, _) = RunCore(LocalPath, "diff", "--cached", "--quiet");
        if (diffExitCode == 0)
            return false;

        Run(LocalPath, "commit", "-m", message);
        Run(LocalPath, "push", "origin", "HEAD");
        return true;
    }

    private void Run(string? workingDir, params string[] args)
    {
        var (exitCode, stdout, stderr) = RunCore(workingDir, args);
        if (exitCode != 0)
            throw new InvalidOperationException(L.Pick(
                $"git {string.Join(' ', args)} failed (exit {exitCode}):\n{stdout}\n{stderr}",
                $"git {string.Join(' ', args)} が失敗しました (exit {exitCode}):\n{stdout}\n{stderr}"));
    }

    private (int ExitCode, string Stdout, string Stderr) RunCore(string? workingDir, params string[] args)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDir ?? "",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        if (!string.IsNullOrWhiteSpace(remote.DeployKeyPath))
        {
            var keyPath = PathUtil.Expand(remote.DeployKeyPath);
            if (File.Exists(keyPath))
                psi.Environment["GIT_SSH_COMMAND"] = $"ssh -i \"{keyPath}\" -o IdentitiesOnly=yes";
        }

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, stdout, stderr);
    }
}
