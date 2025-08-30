using AdhdTimeOrganizer.application.dto.response.@base;

namespace AdhdTimeOrganizer.application.dto.response.activityHistory;

public record ActivityHistoryListGroupedByDateResponse : IMyResponse
{
    public required DateTime Date { get; init; }
    public required List<ActivityHistoryResponse> HistoryResponseList { get; init; }
}