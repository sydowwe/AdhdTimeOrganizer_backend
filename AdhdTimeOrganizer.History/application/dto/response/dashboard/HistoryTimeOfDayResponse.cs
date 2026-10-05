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

    /// <summary>
    /// The hours each day touched, ordered by date, for days of the range only. A day with nothing logged is
    /// left out. So is a record's tail that runs past the range's last midnight: it still counts in
    /// <see cref="Hours"/>, which has to equal the pie-chart total, but it does not add a day the user did
    /// not ask about.
    ///
    /// <para>The day is the one each <i>hour</i> fell on, not the record's start, so a record running
    /// 23:30–00:30 marks hour 23 on one day and hour 0 on the next. <see cref="DaysWithActivity"/> counts
    /// by start, so it can be smaller than this list's length.</para>
    /// </summary>
    public required List<HistoryTimeOfDayDay> Days { get; init; }
}
