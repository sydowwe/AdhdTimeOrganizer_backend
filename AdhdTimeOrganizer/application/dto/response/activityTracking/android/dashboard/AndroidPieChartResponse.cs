namespace AdhdTimeOrganizer.application.dto.response.activityTracking.android.dashboard;

public record AndroidPieChartResponse
{
    public required List<AndroidAppPieData> Apps { get; init; }
    public required AndroidPieTotals Totals { get; init; }
}
