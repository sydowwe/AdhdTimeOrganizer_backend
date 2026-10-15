using AdhdTimeOrganizer.Core.application.dto.dto;

namespace AdhdTimeOrganizer.History.application.dto.request.activityHistory.dashboard.summary;

/// <summary>
/// The range, and nothing else, for the same reason as <see cref="HistorySummaryTimeOfDayRequest"/>: the endpoint
/// sums the range's timer sessions into one set of totals, so there is no group, window or time-of-day clip to pick.
/// A type of its own so its validator can be found by exact type.
/// </summary>
public record HistorySummaryTimerPlanRequest : DateRangeDto;
