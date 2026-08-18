namespace SwitchCloudSaveBrew.Core.Sync;

public enum SyncOutcome
{
    // Local is ahead of (or identical to) remote; local should be pushed.
    LocalWins,
    // Remote is ahead of local; remote should be pulled and applied locally.
    RemoteWins,
    // Same generation, same device, same content hash: nothing to do.
    NoOp,
    // Same generation from different devices with different content: cannot
    // be resolved automatically. Caller must surface this to the user rather
    // than silently pick a side, since it means both sides advanced the
    // generation counter without ever having synced with each other.
    Conflict,
}

public static class ConflictResolver
{
    public static SyncOutcome Resolve(SyncState local, SyncState remote)
    {
        if (local.TitleId != remote.TitleId)
            throw new ArgumentException("cannot compare SyncState for different title_ids");

        if (local.Generation > remote.Generation)
            return SyncOutcome.LocalWins;

        if (local.Generation < remote.Generation)
            return SyncOutcome.RemoteWins;

        // Same generation: identical content is a no-op regardless of device/timestamp.
        if (local.ContentHash == remote.ContentHash)
            return SyncOutcome.NoOp;

        // Same generation, different content: two devices advanced independently
        // without ever observing each other's write. Fall back to timestamp as a
        // best-effort tiebreak, but this situation should be logged/surfaced —
        // it means the generation counter didn't do its job of preventing exactly
        // this ambiguity.
        if (local.UpdatedAtUtc > remote.UpdatedAtUtc)
            return SyncOutcome.Conflict;
        if (local.UpdatedAtUtc < remote.UpdatedAtUtc)
            return SyncOutcome.Conflict;

        return SyncOutcome.Conflict;
    }
}
