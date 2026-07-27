namespace AdhdTimeOrganizer.Scheduler.domain.@enum;

/// <summary>Lifecycle state of a registered job. Changes only on a meaningful pause/resume/remove.</summary>
public enum JobStatus
{
    Active,
    Paused,
    Removed
}