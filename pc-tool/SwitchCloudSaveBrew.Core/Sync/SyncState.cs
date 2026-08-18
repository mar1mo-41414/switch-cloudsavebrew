namespace SwitchCloudSaveBrew.Core.Sync;

// Metadata committed to the repo alongside each save blob (e.g. saves/<title_id>/state.json).
// Generation is the primary conflict-resolution signal because device clocks
// (especially the Switch's RTC) cannot be trusted to agree with each other.
// UpdatedAtUtc is only a tie-breaker for the rare case generations match.
public sealed class SyncState
{
    public string TitleId { get; set; } = "";
    public long Generation { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public string UpdatedByDevice { get; set; } = "";
    public string ContentHash { get; set; } = "";
}
