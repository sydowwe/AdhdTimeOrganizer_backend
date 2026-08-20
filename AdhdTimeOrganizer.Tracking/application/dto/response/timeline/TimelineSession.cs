namespace AdhdTimeOrganizer.Tracking.application.dto.response.activityTracking.timeline;

public record TimelineSession : IDashboardItem
{
    public required long Id { get; set; } // For frontend key
    public required string Domain { get; set; } = string.Empty;

    /// <inheritdoc />
    public string Key => Domain;

    /// <inheritdoc />
    public string Label => Domain;

    public required string? Url { get; set; } // Most visited URL during session
    public required DateTime StartedAt { get; set; }
    public required DateTime EndedAt { get; set; }
    public required int DurationSeconds { get; set; }
    public required int TotalSeconds { get; set; } // Actual activity seconds (may be less than duration)
}