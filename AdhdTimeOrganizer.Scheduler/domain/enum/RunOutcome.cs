namespace AdhdTimeOrganizer.Scheduler.domain.@enum;

/// <summary>
/// Terminal result of a single execution. There is deliberately <b>no</b> "Running"/"InProgress"
/// value — every run row is written once at completion and is terminal (see ScheduledJobRun).
/// </summary>
public enum RunOutcome
{
    Succeeded,
    Failed,
    Skipped,
    Vetoed,
    Misfired,
    Reversed
}