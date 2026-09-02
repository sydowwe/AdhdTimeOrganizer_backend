using AdhdTimeOrganizer.History.application.dto.@enum;

namespace AdhdTimeOrganizer.History.application.dto.request.activityHistory.dashboard.detail;

public record HistoryDetailSummaryCardsRequest : HistoryDetailDateRangeRequest
{
    public HistoryBaseline Baseline { get; init; } = HistoryBaseline.Last7Days;
    public int TopN { get; init; } = 4;
}
