using SwitchCloudSaveBrew.Core;
using SwitchCloudSaveBrew.Core.Config;
using SwitchCloudSaveBrew.Core.Crypto;
using SwitchCloudSaveBrew.Core.Discovery;
using SwitchCloudSaveBrew.Core.Sync;

if (args.Length == 0)
{
    PrintUsage();
    return 1;
}

var command = args[0];
var rest = args[1..];

try
{
    return command switch
    {
        "config" => RunConfig(rest),
        "keys" => RunKeys(rest),
        "save" => RunSave(rest),
        "sync" => RunSync(rest),
        "scan" => RunScan(rest),
        "cloud" => RunCloud(rest),
        _ => Unknown(command),
    };
}
catch (Exception ex)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    return 1;
}

int RunConfig(string[] a)
{
    if (a is not ["validate", var path])
    {
        Console.Error.WriteLine("usage: scsb config validate <config.yaml>");
        return 1;
    }

    var config = ConfigLoader.Load(path);
    Console.WriteLine($"OK: {config.Games.Count} game(s) configured, remote = {config.Remote.GitUrl}");
    return 0;
}

int RunKeys(string[] a)
{
    if (a is not ["check", var path])
    {
        Console.Error.WriteLine("usage: scsb keys check <config.yaml>");
        return 1;
    }

    var config = ConfigLoader.Load(path);
    var keySet = KeySetLoader.Load(config.Keys);
    Console.WriteLine("OK: prod.keys loaded and SD seed applied");
    _ = keySet;
    return 0;
}

int RunSave(string[] a)
{
    if (a is ["decrypt", var config1, var encryptedDir, var plainDir])
    {
        var codec = LoadCodec(config1);
        codec.DecryptDirectory(encryptedDir, plainDir);
        Console.WriteLine($"OK: decrypted {encryptedDir} -> {plainDir}");
        return 0;
    }

    if (a is ["encrypt", var config2, var plainDir2, var encryptedDir2])
    {
        var codec = LoadCodec(config2);
        codec.EncryptDirectory(plainDir2, encryptedDir2);
        Console.WriteLine($"OK: encrypted {plainDir2} -> {encryptedDir2}");
        return 0;
    }

    Console.Error.WriteLine("""
        usage:
          scsb save decrypt <config.yaml> <encrypted-dir> <plain-dir>
          scsb save encrypt <config.yaml> <plain-dir> <encrypted-dir>
        """);
    return 1;
}

int RunSync(string[] a)
{
    if (a is ["push", var configPath, var titleId, ..])
    {
        var (config, target) = ResolveTarget(configPath, titleId, a.Length > 3 ? a[3] : null);
        return SyncPush(config, titleId, target);
    }

    if (a is ["pull", var configPath2, var titleId2, ..])
    {
        var (config, target) = ResolveTarget(configPath2, titleId2, a.Length > 3 ? a[3] : null);
        return SyncPull(config, titleId2, target);
    }

    Console.Error.WriteLine("""
        usage:
          scsb sync push <config.yaml> <title_id> [emulator]
          scsb sync pull <config.yaml> <title_id> [emulator]
        """);
    return 1;
}

int RunScan(string[] a)
{
    if (a is ["list", var configPath, var emulator, var rootDir])
    {
        var config = ConfigLoader.Load(configPath);
        var detected = EmulatorSaveScannerFactory.Get(emulator).Scan(PathUtil.Expand(rootDir));

        if (detected.Count == 0)
        {
            Console.WriteLine("no saves detected under that root");
            return 0;
        }

        foreach (var d in detected)
        {
            var known = config.Games.FirstOrDefault(g => g.TitleId.Equals(d.TitleIdHex, StringComparison.OrdinalIgnoreCase));
            var name = known?.Name ?? "(unknown title)";
            var kind = d.IsFile ? "[file]" : d.IsDeviceSave ? "[dir/device]" : "[dir] ";
            Console.WriteLine($"{d.TitleIdHex}  {kind}  {name}");
            Console.WriteLine($"  {d.LocalPath}");
            if (d.LocalAccountUid is not null)
                Console.WriteLine($"  local account uid: {d.LocalAccountUid}  (put this in config.yaml's accounts: table to map it to a switch_uid)");
        }
        return 0;
    }

    if (a is ["push" or "pull", var configPath2, var emulator2, var rootDir2, var titleId, ..])
    {
        var config = ConfigLoader.Load(configPath2);
        var detected = EmulatorSaveScannerFactory.Get(emulator2).Scan(PathUtil.Expand(rootDir2));
        var match = detected.FirstOrDefault(d => d.TitleIdHex.Equals(titleId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"no detected save for title_id {titleId} under {rootDir2}");

        var target = new TargetConfig
        {
            Emulator = emulator2,
            Os = Environment.OSVersion.Platform.ToString(),
            SavePath = match.LocalPath,
            IsFile = match.IsFile,
            IsDuplex = match.IsDuplex,
            IsDeviceSave = match.IsDeviceSave,
        };

        return a[0] == "push" ? SyncPush(config, titleId, target) : SyncPull(config, titleId, target);
    }

    Console.Error.WriteLine("""
        usage:
          scsb scan list <config.yaml> <ryujinx|eden> <emulator-root-dir>
          scsb scan push <config.yaml> <ryujinx|eden> <emulator-root-dir> <title_id>
          scsb scan pull <config.yaml> <ryujinx|eden> <emulator-root-dir> <title_id>
        """);
    return 1;
}

int RunCloud(string[] a)
{
    if (a is not ["list", var configPath])
    {
        Console.Error.WriteLine("usage: scsb cloud list <config.yaml>");
        return 1;
    }

    var config = ConfigLoader.Load(configPath);
    var entries = CloudListingService.List(config);

    if (entries.Count == 0)
    {
        Console.WriteLine("(no saves in the repo yet)");
        return 0;
    }

    foreach (var entry in entries)
    {
        Console.WriteLine($"{entry.TitleId}  {entry.Name}  [{entry.AccountName}]");
        Console.WriteLine(entry.State is null
            ? "  (no state.json)"
            : $"  gen {entry.State.Generation}, last updated by {entry.State.UpdatedByDevice} at {entry.State.UpdatedAtUtc:u}");
    }

    return 0;
}

(AppConfig Config, TargetConfig Target) ResolveTarget(string configPath, string titleId, string? emulator)
{
    var config = ConfigLoader.Load(configPath);
    var game = config.Games.FirstOrDefault(g => g.TitleId.Equals(titleId, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"no game with title_id {titleId} in config");

    var candidates = emulator is null
        ? game.Targets
        : game.Targets.Where(t => t.Emulator.Equals(emulator, StringComparison.OrdinalIgnoreCase)).ToList();

    if (candidates.Count == 0)
        throw new InvalidOperationException($"no matching target for {titleId}" + (emulator is null ? "" : $" / {emulator}"));
    if (candidates.Count > 1)
        throw new InvalidOperationException(
            $"{candidates.Count} targets match {titleId} — specify [emulator]: " +
            string.Join(", ", candidates.Select(t => $"{t.Emulator}/{t.Os}")));

    return (config, candidates[0]);
}

int SyncPush(AppConfig config, string titleId, TargetConfig target)
{
    var result = SaveSyncService.Push(config, titleId, target);
    Console.WriteLine($"OK: {result.Message}");
    return 0;
}

int SyncPull(AppConfig config, string titleId, TargetConfig target)
{
    var result = SaveSyncService.Pull(config, titleId, target, preview =>
    {
        Console.WriteLine($"About to overwrite {preview.SavePath} with repo gen {preview.RemoteGeneration} " +
            $"(from {preview.UpdatedByDevice} at {preview.UpdatedAtUtc:u}). Continue? [y/N]");
        return Console.ReadLine()?.Trim().ToLowerInvariant() == "y";
    });

    if (result.Status == SyncStatus.Aborted)
    {
        Console.WriteLine(result.Message);
        return 1;
    }

    Console.WriteLine($"OK: {result.Message}");
    return 0;
}

LibHacSaveDataCodec LoadCodec(string configPath)
{
    var config = ConfigLoader.Load(configPath);
    var keySet = KeySetLoader.Load(config.Keys);
    return new LibHacSaveDataCodec(keySet);
}

int Unknown(string cmd)
{
    Console.Error.WriteLine($"unknown command: {cmd}");
    PrintUsage();
    return 1;
}

void PrintUsage()
{
    Console.Error.WriteLine("""
        usage:
          scsb config validate <config.yaml>
          scsb keys check <config.yaml>
          scsb save decrypt <config.yaml> <encrypted-dir> <plain-dir>
          scsb save encrypt <config.yaml> <plain-dir> <encrypted-dir>
          scsb sync push <config.yaml> <title_id> [emulator]
          scsb sync pull <config.yaml> <title_id> [emulator]
          scsb scan list <config.yaml> <ryujinx|eden> <emulator-root-dir>
          scsb scan push <config.yaml> <ryujinx|eden> <emulator-root-dir> <title_id>
          scsb scan pull <config.yaml> <ryujinx|eden> <emulator-root-dir> <title_id>
          scsb cloud list <config.yaml>
        """);
}
