using AdhdTimeOrganizer.Core.application.dto.dto;

namespace AdhdTimeOrganizer.History.application.dto.request.activityHistory.dashboard.summary;

/// <summary>
/// The range, and nothing else. Deliberately <b>not</b> a <see cref="HistorySummaryDateRangeRequest"/>:
/// this endpoint folds the whole range into hours of day, so there is no group to pick, no window width to
/// pick and no time-of-day window to clip by.
///
/// <para>That absence is the point of the endpoint. The stacked-bars response can be folded by hour of day
/// client-side, but its buckets are <c>WindowMinutes</c> wide and every day is clipped to a
/// <c>WindowStartTime</c>/<c>WindowEndTime</c> the user picks from the chart controls — so the fold answers
/// a question about the controls, not about the history. Adding any of those three fields here would
/// reintroduce exactly that.</para>
/// </summary>
public record HistorySummaryTimeOfDayRequest : DateRangeDto;
