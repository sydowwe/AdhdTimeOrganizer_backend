using AdhdTimeOrganizer.Common.application.dto.response.@base;

namespace AdhdTimeOrganizer.Command.application.dto.response.activityHistory;

public record ActivityHistoryListGroupedByDateResponse(
    DateTime Date,
    List<ActivityHistoryResponse> HistoryResponseList
) : IMyResponse;