namespace AdhdTimeOrganizer.History.application.dto.@enum;

/// <summary>
/// The stretch of past a summary card's <c>averageSeconds</c> and <c>percentChange</c> are measured
/// against. Resolved to an actual range by <c>HistoryBaselineWindow.Resolve</c>.
/// </summary>
/// <remarks>
/// This was a <c>string</c> defaulted to <c>"last7days"</c>, lower-cased at the call site and matched in
/// two <c>switch</c>es whose <c>default</c> arm silently meant <c>last7days</c> — so a client typo was
/// answered 200 with a wrong-but-plausible baseline, the exact silent failure this slice is supposed to
/// design against. As an enum an unknown value is a 400 from the model binder, and both switches are
/// exhaustive with a throwing default that a new member trips immediately.
///
/// <para>The wire format is the member name (<c>JsonStringEnumConverter</c>, no naming policy) and
/// System.Text.Json matches it case-insensitively, so the strings clients already send —
/// <c>"last7days"</c>, <c>"last30days"</c>, <c>"sameWeekday"</c>, <c>"allTime"</c> — keep binding
/// unchanged.</para>
/// </remarks>
public enum HistoryBaseline
{
    /// <summary>The seven days before the reported period. The default.</summary>
    Last7Days,

    /// <summary>The thirty days before the reported period.</summary>
    Last30Days,

    /// <summary>
    /// The eight weeks before the reported period, keeping only the weekdays the reported period itself
    /// covers — so a Monday is compared against Mondays.
    /// </summary>
    SameWeekday,

    /// <summary>
    /// Everything before the reported period, with no lower bound at all; the day count is derived from
    /// the oldest row found.
    /// </summary>
    AllTime
}
