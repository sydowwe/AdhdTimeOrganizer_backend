using Sydowwe.Framework.application.dto.response.@base;

namespace AdhdTimeOrganizer.History.application.dto.response.activityHistory;

public record ActivityHistoryListGroupedByDateResponse : IMyResponse
{
    public required DateTime Date { get; init; }
    public required IEnumerable<ActivityHistoryResponse> HistoryResponseList { get; init; }
}