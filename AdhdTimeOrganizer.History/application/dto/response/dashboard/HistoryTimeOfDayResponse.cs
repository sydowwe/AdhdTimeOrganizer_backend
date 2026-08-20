namespace AdhdTimeOrganizer.History.application.dto.response.activityHistory.dashboard;

/// <summary>
/// Where in the day a user's logged time sits, folded over a multi-day range at hour resolution.
/// </summary>
public record HistoryTimeOfDayResponse
{
    /// <summary>
    /// Always 24 entries, ordered <c>Hour</c> 0-23, including hours with no activity. The client indexes
    /// this rather than sorting or padding it, so an endpoint that omitted empty hours would silently
    /// misalign every bucket after the first gap.
    /// </summary>
    public required List<HistoryTimeOfDayHour> Hours { get; init; }

    /// <summary>Calendar days the range covers, so a client can turn a total into a per-day figure.</summary>
    public required int DaysInRange { get; init; }

    /// <summary>
    /// Days of the range with at least one record, by the record's start on the user's clock. The client
    /// uses this as the threshold below which it does not render the insight at all.
    /// </summary>
    public required int DaysWithActivity { get; init; }
}
