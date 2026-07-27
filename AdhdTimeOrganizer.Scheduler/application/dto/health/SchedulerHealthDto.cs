using AdhdTimeOrganizer.Scheduler.application.dto.scheduledJob;
using AdhdTimeOrganizer.Scheduler.domain.@enum;

namespace AdhdTimeOrganizer.Scheduler.application.dto.health;

/// <summary>
/// The "needs attention" projection over the registry + the append-only run log. Status counts, recent run
/// outcomes over a window, and the three actionable problem lists: jobs whose last run failed, jobs that are
/// overdue (Active but past the grace margin), and orphaned registry rows (handler no longer registered).
/// </summary>
public record SchedulerHealthDto
{
    public required int ActiveCount { get; init; }
    public required int PausedCount { get; init; }
    public required int RemovedCount { get; init; }

    /// <summary>Window (hours) the <see cref="RecentRunOutcomes"/> counts cover.</summary>
    public required int RecentWindowHours { get; init; }

    public required List<RunOutcomeCountDto> RecentRunOutcomes { get; init; }

    public required List<ScheduledJobDto> FailedJobs { get; init; }
    public required List<ScheduledJobDto> OverdueJobs { get; init; }
    public required List<ScheduledJobDto> OrphanedJobs { get; init; }
}

/// <summary>One outcome bucket of the recent-runs window.</summary>
public record RunOutcomeCountDto
{
    public required RunOutcome Outcome { get; init; }
    public required int Count { get; init; }
}