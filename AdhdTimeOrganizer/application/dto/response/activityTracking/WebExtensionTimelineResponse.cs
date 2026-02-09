namespace AdhdTimeOrganizer.application.dto.response.activityTracking;

public record WebExtensionTimelineResponse
{
    public List<TimelineSession> ActiveSessions { get; set; } = new();
    public List<TimelineSession> BackgroundSessions { get; set; } = new();
    public DateTime From { get; set; }
    public DateTime To { get; set; }
}

public class TimelineSession
{
    public long Id { get; set; }  // For frontend key
    public string Domain { get; set; } = string.Empty;
    public string? Url { get; set; }  // Most visited URL during session
    public DateTime StartedAt { get; set; }
    public DateTime EndedAt { get; set; }
    public int DurationSeconds { get; set; }
    public int TotalSeconds { get; set; }  // Actual activity seconds (may be less than duration)
}
