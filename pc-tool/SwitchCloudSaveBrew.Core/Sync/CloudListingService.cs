using SwitchCloudSaveBrew.Core.Config;

namespace SwitchCloudSaveBrew.Core.Sync;

public sealed class CloudSaveEntry
{
    public required string TitleId { get; init; }
    public required string Name { get; init; }

    // The real Switch account uid (hex), or AccountUidResolver.DeviceSegment
    // for device-type saves.
    public required string AccountSegment { get; init; }

    // The accounts: entry's display name for AccountSegment, if config.yaml
    // maps it to one — otherwise just AccountSegment itself.
    public required string AccountName { get; init; }

    public SyncState? State { get; init; }
}

// What's in the switch-savedata repo right now — shared by the CLI's
// `cloud list` and the GUI's Cloud tab so the repo-walking logic only
// lives in one place.
public static class CloudListingService
{
    public static IReadOnlyList<CloudSaveEntry> List(AppConfig config)
    {
        var repo = new RepoSync(config.Remote);
        repo.EnsureUpToDate();

        var savesDir = Path.Combine(repo.LocalPath, "saves");
        if (!Directory.Exists(savesDir))
            return [];

        var results = new List<CloudSaveEntry>();
        foreach (var titleDir in Directory.EnumerateDirectories(savesDir).OrderBy(d => d))
        {
            var titleId = Path.GetFileName(titleDir)!;
            var known = config.Games.FirstOrDefault(g => g.TitleId.Equals(titleId, StringComparison.OrdinalIgnoreCase));
            var name = known?.Name ?? "(not in this config.yaml)";

            foreach (var accountDir in Directory.EnumerateDirectories(titleDir).OrderBy(d => d))
            {
                var accountSegment = Path.GetFileName(accountDir)!;
                var state = SyncStateStore.TryLoad(Path.Combine(accountDir, "state.json"));

                var account = config.Accounts.FirstOrDefault(a =>
                    a.SwitchUid.Equals(accountSegment, StringComparison.OrdinalIgnoreCase));

                var accountName = accountSegment == AccountUidResolver.DeviceSegment
                    ? "device"
                    : account?.Name ?? accountSegment;

                results.Add(new CloudSaveEntry
                {
                    TitleId = titleId,
                    Name = name,
                    AccountSegment = accountSegment,
                    AccountName = accountName,
                    State = state,
                });
            }
        }

        return results;
    }
}
