using AdhdTimeOrganizer.Tracking.application.dto.@enum;

namespace AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking;

public record SummaryCardsRequest : DateRangeAndTimeRangeDto
{
    public int? TopN { get; init; } // Optional, default null

    /// <summary>
    /// The lookback the cards compare against, still read as "the average <b>day</b> over that
    /// lookback" — the mean day is then multiplied by <c>DayCount</c> so the comparison is like for
    /// like against a card whose own number is a span total. <c>SameWeekday</c> stops meaning much over
    /// a span longer than a week; it is left selectable rather than silently reinterpreted.
    /// </summary>
    public BaselineType Baseline { get; init; } = BaselineType.Last7Days;
}