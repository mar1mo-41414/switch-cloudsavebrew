using SwitchCloudSaveBrew.Core.Config;
using SwitchCloudSaveBrew.Core.Discovery;

namespace SwitchCloudSaveBrew.Core.Sync;

// Resolves which cloud account bucket (saves/<title_id>/<segment>/...) a
// target's save data belongs to. Device-type saves have no account concept
// at all (one shared save per console) and always use the fixed "device"
// bucket. Account-type saves need the emulator's own local profile uid
// resolved (from the save data itself) and looked up against config.yaml's
// accounts: table to find the corresponding real Switch account uid — the
// two are unrelated identifier namespaces (each emulator assigns its own
// local profile ids independent of any real Nintendo Account), so this
// mapping has to be explicit, never guessed.
public static class AccountUidResolver
{
    public const string DeviceSegment = "device";

    public static string ResolveSegment(AppConfig config, TargetConfig target)
    {
        if (target.IsDeviceSave)
            return DeviceSegment;

        var localUid = ResolveLocalUid(target)
            ?? throw new InvalidOperationException(
                $"couldn't determine the local account uid for the {target.Emulator} save at " +
                $"{target.SavePath} (has this title been run locally under the intended profile yet?)");

        var account = config.Accounts.FirstOrDefault(a =>
            string.Equals(a.RyujinxUid, localUid, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(a.EdenUid, localUid, StringComparison.OrdinalIgnoreCase));

        if (account is null || string.IsNullOrWhiteSpace(account.SwitchUid))
            throw new InvalidOperationException(
                $"no accounts: entry in config.yaml maps {target.Emulator} local uid {localUid} to a switch_uid " +
                "(see the accounts: section in config.example.yaml)");

        return account.SwitchUid.ToUpperInvariant();
    }

    public static string? ResolveLocalUid(TargetConfig target)
    {
        var savePath = PathUtil.Expand(target.SavePath);
        return target.Emulator.ToLowerInvariant() switch
        {
            "ryujinx" => RyujinxSaveScanner.ReadLocalAccountUid(savePath),
            "eden" => EdenSaveScanner.ReadLocalAccountUid(savePath, target.IsFile),
            _ => null,
        };
    }
}
