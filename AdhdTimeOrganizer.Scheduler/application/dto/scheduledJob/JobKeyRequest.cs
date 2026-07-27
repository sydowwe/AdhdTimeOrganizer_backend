namespace AdhdTimeOrganizer.Scheduler.application.dto.scheduledJob;

/// <summary>Identifies a single registered job by its business <c>JobKey</c> for remove/pause/resume/trigger-now.</summary>
public record JobKeyRequest
{
    public required string JobKey { get; init; }
}