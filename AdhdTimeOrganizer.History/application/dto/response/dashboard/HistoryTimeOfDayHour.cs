namespace AdhdTimeOrganizer.History.application.dto.response.activityHistory.dashboard;

/// <summary>
/// One hour of day, summed over every day of the range.
/// </summary>
public record HistoryTimeOfDayHour
{
    /// <summary>0-23, on the requesting user's clock (<c>User.Timezone</c>), never UTC.</summary>
    public required int Hour { get; init; }

    /// <summary>
    /// Seconds logged in this hour of day across the range. Zero for an hour with no activity — the
    /// response always carries all 24 hours.
    /// </summary>
    public required long TotalSeconds { get; init; }

    /// <summary>
    /// Records contributing to this hour. A record that spans an hour boundary is counted in <b>every</b>
    /// hour it touches, so summing this across the 24 hours does not give the period's record count.
    /// </summary>
    public required int Entries { get; init; }
}
