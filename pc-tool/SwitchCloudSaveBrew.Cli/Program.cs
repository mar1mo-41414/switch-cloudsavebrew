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
    Console.Error.WriteLine(L.Pick($"error: {ex.Message}", $"エラー: {ex.Message}"));
    return 1;
}

int RunConfig(string[] a)
{
    if (a is not ["validate", var path])
    {
        Console.Error.WriteLine(L.Pick("usage: scsb config validate <config.yaml>", "使い方: scsb config validate <config.yaml>"));
        return 1;
    }

    var config = ConfigLoader.Load(path);
    Console.WriteLine(L.Pick(
        $"OK: {config.Games.Count} game(s) configured, remote = {config.Remote.GitUrl}",
        $"OK: {config.Games.Count}件のゲームを設定済み, remote = {config.Remote.GitUrl}"));
    return 0;
}

int RunKeys(string[] a)
{
    if (a is not ["check", var path])
    {
        Console.Error.WriteLine(L.Pick("usage: scsb keys check <config.yaml>", "使い方: scsb keys check <config.yaml>"));
        return 1;
    }

    var config = ConfigLoader.Load(path);
    var keySet = KeySetLoader.Load(config.Keys);
    Console.WriteLine(L.Pick("OK: prod.keys loaded and SD seed applied", "OK: prod.keysを読み込み、SD seedを適用しました"));
    _ = keySet;
    return 0;
}

int RunSave(string[] a)
{
    if (a is ["decrypt", var config1, var encryptedDir, var plainDir])
    {
        var codec = LoadCodec(config1);
        codec.DecryptDirectory(encryptedDir, plainDir);
        Console.WriteLine(L.Pick($"OK: decrypted {encryptedDir} -> {plainDir}", $"OK: 復号しました {encryptedDir} -> {plainDir}"));
        return 0;
    }

    if (a is ["encrypt", var config2, var plainDir2, var encryptedDir2])
    {
        var codec = LoadCodec(config2);
        codec.EncryptDirectory(plainDir2, encryptedDir2);
        Console.WriteLine(L.Pick($"OK: encrypted {plainDir2} -> {encryptedDir2}", $"OK: 暗号化しました {plainDir2} -> {encryptedDir2}"));
        return 0;
    }

    Console.Error.WriteLine(L.Pick(
        """
        usage:
          scsb save decrypt <config.yaml> <encrypted-dir> <plain-dir>
          scsb save encrypt <config.yaml> <plain-dir> <encrypted-dir>
        """,
        """
        使い方:
          scsb save decrypt <config.yaml> <encrypted-dir> <plain-dir>
          scsb save encrypt <config.yaml> <plain-dir> <encrypted-dir>
        """));
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

    Console.Error.WriteLine(L.Pick(
        """
        usage:
          scsb sync push <config.yaml> <title_id> [emulator]
          scsb sync pull <config.yaml> <title_id> [emulator]
        """,
        """
        使い方:
          scsb sync push <config.yaml> <title_id> [emulator]
          scsb sync pull <config.yaml> <title_id> [emulator]
        """));
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
            Console.WriteLine(L.Pick("no saves detected under that root", "そのルート配下にセーブは検出されませんでした"));
            return 0;
        }

        foreach (var d in detected)
        {
            var known = config.Games.FirstOrDefault(g => g.TitleId.Equals(d.TitleIdHex, StringComparison.OrdinalIgnoreCase));
            var name = known?.Name ?? L.Pick("(unknown title)", "(不明なタイトル)");
            var kind = d.IsFile ? "[file]" : d.IsDeviceSave ? "[dir/device]" : "[dir] ";
            Console.WriteLine($"{d.TitleIdHex}  {kind}  {name}");
            Console.WriteLine($"  {d.LocalPath}");
            if (d.LocalAccountUid is not null)
                Console.WriteLine(L.Pick(
                    $"  local account uid: {d.LocalAccountUid}  (put this in config.yaml's accounts: table to map it to a switch_uid)",
                    $"  ローカルアカウントuid: {d.LocalAccountUid}  (config.yamlのaccounts:テーブルでswitch_uidに紐付けてください)"));
        }
        return 0;
    }

    if (a is ["push" or "pull", var configPath2, var emulator2, var rootDir2, var titleId, ..])
    {
        var config = ConfigLoader.Load(configPath2);
        var detected = EmulatorSaveScannerFactory.Get(emulator2).Scan(PathUtil.Expand(rootDir2));
        var match = detected.FirstOrDefault(d => d.TitleIdHex.Equals(titleId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(L.Pick(
                $"no detected save for title_id {titleId} under {rootDir2}",
                $"{rootDir2} 配下にtitle_id {titleId} の検出セーブがありません"));

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

    Console.Error.WriteLine(L.Pick(
        """
        usage:
          scsb scan list <config.yaml> <ryujinx|eden> <emulator-root-dir>
          scsb scan push <config.yaml> <ryujinx|eden> <emulator-root-dir> <title_id>
          scsb scan pull <config.yaml> <ryujinx|eden> <emulator-root-dir> <title_id>
        """,
        """
        使い方:
          scsb scan list <config.yaml> <ryujinx|eden> <emulator-root-dir>
          scsb scan push <config.yaml> <ryujinx|eden> <emulator-root-dir> <title_id>
          scsb scan pull <config.yaml> <ryujinx|eden> <emulator-root-dir> <title_id>
        """));
    return 1;
}

int RunCloud(string[] a)
{
    if (a is not ["list", var configPath])
    {
        Console.Error.WriteLine(L.Pick("usage: scsb cloud list <config.yaml>", "使い方: scsb cloud list <config.yaml>"));
        return 1;
    }

    var config = ConfigLoader.Load(configPath);
    var entries = CloudListingService.List(config);

    if (entries.Count == 0)
    {
        Console.WriteLine(L.Pick("(no saves in the repo yet)", "(リポジトリにまだセーブがありません)"));
        return 0;
    }

    foreach (var entry in entries)
    {
        Console.WriteLine($"{entry.TitleId}  {entry.Name}  [{entry.AccountName}]");
        Console.WriteLine(entry.State is null
            ? L.Pick("  (no state.json)", "  (state.jsonなし)")
            : L.Pick(
                $"  gen {entry.State.Generation}, last updated by {entry.State.UpdatedByDevice} at {entry.State.UpdatedAtUtc:u}",
                $"  gen {entry.State.Generation}, 最終更新: {entry.State.UpdatedByDevice} ({entry.State.UpdatedAtUtc:u})"));
    }

    return 0;
}

(AppConfig Config, TargetConfig Target) ResolveTarget(string configPath, string titleId, string? emulator)
{
    var config = ConfigLoader.Load(configPath);
    var game = config.Games.FirstOrDefault(g => g.TitleId.Equals(titleId, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException(L.Pick($"no game with title_id {titleId} in config", $"configにtitle_id {titleId} のゲームがありません"));

    var candidates = emulator is null
        ? game.Targets
        : game.Targets.Where(t => t.Emulator.Equals(emulator, StringComparison.OrdinalIgnoreCase)).ToList();

    if (candidates.Count == 0)
        throw new InvalidOperationException(L.Pick(
            $"no matching target for {titleId}" + (emulator is null ? "" : $" / {emulator}"),
            $"{titleId}" + (emulator is null ? "" : $" / {emulator}") + " に一致するtargetがありません"));
    if (candidates.Count > 1)
        throw new InvalidOperationException(L.Pick(
            $"{candidates.Count} targets match {titleId} — specify [emulator]: " +
            string.Join(", ", candidates.Select(t => $"{t.Emulator}/{t.Os}")),
            $"{titleId} に一致するtargetが{candidates.Count}件あります — [emulator]を指定してください: " +
            string.Join(", ", candidates.Select(t => $"{t.Emulator}/{t.Os}"))));

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
        Console.WriteLine(L.Pick(
            $"About to overwrite {preview.SavePath} with repo gen {preview.RemoteGeneration} " +
            $"(from {preview.UpdatedByDevice} at {preview.UpdatedAtUtc:u}). Continue? [y/N]",
            $"{preview.SavePath} をリポジトリのgen {preview.RemoteGeneration}で上書きします " +
            $"({preview.UpdatedByDevice} が {preview.UpdatedAtUtc:u} に更新)。続けますか? [y/N]"));
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
    Console.Error.WriteLine(L.Pick($"unknown command: {cmd}", $"不明なコマンド: {cmd}"));
    PrintUsage();
    return 1;
}

void PrintUsage()
{
    Console.Error.WriteLine(L.Pick(
        """
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
        """,
        """
        使い方:
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
        """));
}
