using AdhdTimeOrganizer.Scheduler.domain.@enum;

namespace AdhdTimeOrganizer.Scheduler.application.dto.scheduledJob;

/// <summary>
/// Dashboard jobs-overview filter. Extends the 02b diagnostic-grid filter with the two things the dashboard
/// list adds over it: a <see cref="LastOutcome"/> filter and the stuck/overdue signal (<see cref="OnlyOverdue"/>).
/// </summary>
public record ScheduledJobsOverviewFilterRequest : ScheduledJobFilterRequest
{
    public RunOutcome? LastOutcome { get; set; }

    /// <summary>When true, return only Active jobs whose <c>NextRunAt</c> is past the overdue grace margin.</summary>
    public bool? OnlyOverdue { get; set; }
}