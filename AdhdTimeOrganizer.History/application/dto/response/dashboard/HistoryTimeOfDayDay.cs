namespace AdhdTimeOrganizer.History.application.dto.response.activityHistory.dashboard;

/// <summary>
/// Which hours of one day of the range had anything logged. The client uses it to count the days a band of
/// hours was used: "mornings, on 12 of 30 days".
///
/// <para>The 24 hour totals cannot say that. Three long mornings and twelve short ones can fold into the
/// same buckets, and <see cref="HistoryTimeOfDayHour.Entries"/> counts records, not days. The client finds
/// the band, so the server cannot count days for it. It sends the days themselves, one small int each.</para>
/// </summary>
public record HistoryTimeOfDayDay
{
    /// <summary>The day on the user's clock (<c>User.Timezone</c>).</summary>
    public required DateOnly Date { get; init; }

    /// <summary>
    /// Bit <c>h</c> is set when a record touched hour <c>h</c> on this day, by the same rule as
    /// <see cref="HistoryTimeOfDayHour.Entries"/>: a record crossing an hour boundary touches every hour it
    /// covers, and its part after midnight lands on the next day. Never zero, because a day with nothing
    /// logged is left out.
    /// </summary>
    public required int HourMask { get; init; }
}
