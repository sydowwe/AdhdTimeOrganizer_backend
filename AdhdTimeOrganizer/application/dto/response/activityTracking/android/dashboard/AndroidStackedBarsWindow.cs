namespace AdhdTimeOrganizer.application.dto.response.activityTracking.android.dashboard;

public record AndroidStackedBarsWindow
{
    public DateTime WindowStart { get; init; }
    public DateTime WindowEnd { get; init; }
    public List<AndroidWindowApp> Apps { get; init; } = new();
}
