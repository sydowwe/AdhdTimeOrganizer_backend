namespace AdhdTimeOrganizer.History.application.dto.response.activityHistory;

/// <summary>
/// One activity's logged-time totals across all of the calling user's history. Only activities with at
/// least one logged row are returned, so <see cref="EntryCount"/> is always ≥ 1 and the caller can
/// divide by it without guarding.
/// </summary>
public record ActivityHistoryAggregateByActivityResponse
{
    public required long ActivityId { get; init; }
    public required long TotalSeconds { get; init; }
    public required int EntryCount { get; init; }
}
