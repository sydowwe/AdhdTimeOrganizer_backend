using AdhdTimeOrganizer.Common.application.dto.response.@base;

namespace AdhdTimeOrganizer.Command.application.dto.response.activityHistory;

public record ActivityHistoryListGroupedByDateResponse : IMyResponse
{
    public required DateTime Date { get; init; }
    public required List<ActivityHistoryResponse> HistoryResponseList { get; init; }
}