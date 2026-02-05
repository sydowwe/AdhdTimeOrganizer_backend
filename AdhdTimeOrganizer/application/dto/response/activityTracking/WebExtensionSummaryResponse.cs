namespace AdhdTimeOrganizer.application.dto.response.activityTracking;

public record WebExtensionSummaryResponse{
    public List<WebExtensionSummaryWindow> Windows { get; set; } = new();
}