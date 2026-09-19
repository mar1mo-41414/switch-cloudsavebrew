using SwitchCloudSaveBrew.Core.Config;

namespace SwitchCloudSaveBrew.Core.Sync;

public enum SyncStatus
{
    Ok,
    UpToDate,
    Aborted,
}

public sealed class PushResult
{
    public required SyncStatus Status { get; init; }
    public required string Message { get; init; }
    public long? Generation { get; init; }
}

// What the caller needs to decide whether to let a pull overwrite the local
// save. Kept separate from SyncState so callers (CLI console prompt, GUI
// dialog) don't need to know about the repo's on-disk state format.
public sealed class PullPreview
{
    public required long RemoteGeneration { get; init; }
    public required string UpdatedByDevice { get; init; }
    public required DateTimeOffset UpdatedAtUtc { get; init; }
    public required string SavePath { get; init; }
}

public sealed class PullResult
{
    public required SyncStatus Status { get; init; }
    public required string Message { get; init; }
    public long? Generation { get; init; }
}

// The actual push/pull orchestration (generation-counter LWW, repo sync,
// state.json bookkeeping) — shared by the CLI and any other frontend (GUI)
// so the logic only lives in one place. Callers own how they present
// progress and the pull confirmation; this layer does no console/UI I/O
// itself.
public static class SaveSyncService
{
    public static PushResult Push(AppConfig config, string titleId, TargetConfig target)
    {
        var savePath = PathUtil.Expand(target.SavePath);
        if (target.IsFile && !File.Exists(savePath))
            throw new FileNotFoundException(L.Pick($"save file not found: {savePath}", $"セーブファイルが見つかりません: {savePath}"), savePath);
        if (!target.IsFile && !Directory.Exists(savePath))
            throw new DirectoryNotFoundException(L.Pick($"save directory not found: {savePath}", $"セーブディレクトリが見つかりません: {savePath}"));

        var accountSegment = AccountUidResolver.ResolveSegment(config, target);

        var repo = new RepoSync(config.Remote);
        repo.EnsureUpToDate();

        var repoDataDir = Path.Combine(repo.LocalPath, "saves", titleId, accountSegment, "data");
        var repoStatePath = Path.Combine(repo.LocalPath, "saves", titleId, accountSegment, "state.json");

        // For a duplex target, savePath is the save-id dir containing 0/
        // and/or 1/ — resolve down to whichever copy is canonical before
        // hashing/mirroring from it.
        var sourceDir = target.IsDuplex ? DirectoryMirror.PickDuplexSource(savePath) : savePath;

        var localHash = target.IsFile ? SaveHasher.HashFile(savePath) : SaveHasher.HashDirectory(sourceDir);
        var remoteState = SyncStateStore.TryLoad(repoStatePath);

        if (remoteState is not null && remoteState.ContentHash == localHash)
            return new PushResult { Status = SyncStatus.UpToDate, Message = L.Pick("no changes, nothing to push", "変更なし、pushするものがありません") };

        var newState = new SyncState
        {
            TitleId = titleId,
            Generation = (remoteState?.Generation ?? 0) + 1,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedByDevice = Environment.MachineName,
            ContentHash = localHash,
        };

        if (target.IsFile)
        {
            FileMirror.Mirror(savePath, repoDataDir);
        }
        else
        {
            DirectoryMirror.Mirror(sourceDir, repoDataDir);
            DirectoryMirror.AddGitPlaceholders(repoDataDir);
        }
        SyncStateStore.Save(repoStatePath, newState);

        var pushed = repo.CommitAndPush($"sync: push {titleId} gen {newState.Generation} from {newState.UpdatedByDevice}");
        SyncStateStore.Save(LocalStateCache.PathFor(titleId, accountSegment, target.Emulator, target.Os), newState);

        return pushed
            ? new PushResult { Status = SyncStatus.Ok, Message = L.Pick($"pushed gen {newState.Generation}", $"gen {newState.Generation} をpushしました"), Generation = newState.Generation }
            : new PushResult { Status = SyncStatus.UpToDate, Message = L.Pick(
                "committed nothing new (hash matched after mirroring — no-op)",
                "コミットする変更はありませんでした(ミラー後にハッシュが一致 — 何もしていません)") };
    }

    // confirmOverwrite is called only when there's actually something newer
    // to deploy; returning false aborts without touching the local save.
    public static PullResult Pull(AppConfig config, string titleId, TargetConfig target, Func<PullPreview, bool> confirmOverwrite)
    {
        var accountSegment = AccountUidResolver.ResolveSegment(config, target);

        var repo = new RepoSync(config.Remote);
        repo.EnsureUpToDate();

        var repoDataDir = Path.Combine(repo.LocalPath, "saves", titleId, accountSegment, "data");
        var repoStatePath = Path.Combine(repo.LocalPath, "saves", titleId, accountSegment, "state.json");

        var remoteState = SyncStateStore.TryLoad(repoStatePath)
            ?? throw new InvalidOperationException(L.Pick(
                $"no data in repo yet for {titleId} ({accountSegment}) — push from somewhere first",
                $"{titleId} ({accountSegment}) のデータはまだリポジトリにありません — どこかからpushしてください"));

        var localCachePath = LocalStateCache.PathFor(titleId, accountSegment, target.Emulator, target.Os);
        var localKnown = SyncStateStore.TryLoad(localCachePath);

        if (localKnown is not null && localKnown.Generation >= remoteState.Generation)
            return new PullResult { Status = SyncStatus.UpToDate, Message = L.Pick($"already up to date (gen {localKnown.Generation})", $"既に最新です (gen {localKnown.Generation})"), Generation = localKnown.Generation };

        var savePath = PathUtil.Expand(target.SavePath);

        var proceed = confirmOverwrite(new PullPreview
        {
            RemoteGeneration = remoteState.Generation,
            UpdatedByDevice = remoteState.UpdatedByDevice,
            UpdatedAtUtc = remoteState.UpdatedAtUtc,
            SavePath = savePath,
        });
        if (!proceed)
            return new PullResult { Status = SyncStatus.Aborted, Message = L.Pick("aborted", "中断しました") };

        if (target.IsFile)
        {
            FileMirror.Deploy(repoDataDir, savePath);
        }
        else if (target.IsDuplex)
        {
            DirectoryMirror.MirrorDuplex(repoDataDir, savePath);
        }
        else
        {
            DirectoryMirror.Mirror(repoDataDir, savePath);
            DirectoryMirror.RemoveGitPlaceholders(savePath);
        }
        SyncStateStore.Save(localCachePath, remoteState);

        return new PullResult { Status = SyncStatus.Ok, Message = L.Pick(
            $"deployed gen {remoteState.Generation} to {savePath}",
            $"gen {remoteState.Generation} を {savePath} へ反映しました"), Generation = remoteState.Generation };
    }
}
