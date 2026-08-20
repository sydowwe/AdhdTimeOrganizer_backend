using AdhdTimeOrganizer.Tracking.application.dto.@enum;

namespace AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking;

/// <summary>
/// The fragmentation dashboard's request. The span members mean exactly what they mean on the other
/// four dashboards — a time-of-day window repeated on each day — and unlike the timeline this one
/// <b>does</b> support a range, because that is half of why it exists: over a range the client has no
/// sessions to derive these numbers from itself.
/// </summary>
public record FocusMetricsRequest : DateRangeAndTimeRangeDto
{
    /// <summary>
    /// The lookback to compare against, or <c>null</c> for no comparison at all — which is a request
    /// the client makes deliberately, not a degenerate one. Read the same way
    /// <see cref="SummaryCardsRequest.Baseline"/> is, because the two are shown on the same screen and
    /// disagreeing about what "compared to last 7 days" means is worse than either answer.
    /// <para>Nullable here and non-nullable there on purpose: summary-cards always compares.</para>
    /// </summary>
    public BaselineType? Baseline { get; init; }

    /// <summary>
    /// How long an interruption may be before it breaks the longest-block measure.
    ///
    /// <para><b>Deliberately not a constant on this side.</b> Where the line sits between "a glance at
    /// another window" and "the block ended" is a judgment about what to show a user, not a fact about
    /// the data, so it lives in the client as a named constant and is surfaced in its UI. It has to
    /// travel with the request because the same screen shows a client-computed number (single day,
    /// derived from timeline sessions) beside a server-computed one (a range, from here) — hardcoding
    /// 120 here would make the same day read differently depending on how the user reached it.</para>
    ///
    /// <para>The default is only the fallback for a caller that omits it entirely; the client sends the
    /// value every time.</para>
    /// </summary>
    public int FocusGapSeconds { get; init; } = 120;
}
