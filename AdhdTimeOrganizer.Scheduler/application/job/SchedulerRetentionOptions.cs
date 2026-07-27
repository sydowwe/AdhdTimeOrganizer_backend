using Sydowwe.Framework.infrastructure.persistence.retention;

namespace AdhdTimeOrganizer.Scheduler.application.job;

/// <summary>
/// Retention policy for the Scheduler's own run log (<c>scheduled_job_run</c>), bound from the
/// "SchedulerRetention" config section. Pure <see cref="RetentionOptions"/> — the ledger needs no knobs
/// beyond the shared three, and the defaults (3 years, keep last 20 runs per job) work out of the box.
/// </summary>
public sealed class SchedulerRetentionOptions : RetentionOptions
{
    public const string SectionName = "SchedulerRetention";
}