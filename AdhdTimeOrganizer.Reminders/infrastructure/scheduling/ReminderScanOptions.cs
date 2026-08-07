using Sydowwe.Framework.Contracts.scheduling;

namespace AdhdTimeOrganizer.Reminders.infrastructure.scheduling;

/// <summary>
/// The configurable cadence of the single recurring reminders scan job (phase 03b). Bound from the
/// "ReminderScan" config section; the defaults work out of the box with no configuration.
/// <para>
/// The cadence is the <b>firing-precision floor</b>: a reminder fires within one cadence of its instant.
/// The default of every <b>5 minutes</b> is ample for deadline / lead-offset reminders and cheap on a
/// small-company single node. Set <see cref="Cron"/> for an irregular cadence (it overrides
/// <see cref="CadenceMinutes"/>).
/// </para>
/// <para>
/// This <i>scan-cadence</i> schedule is distinct from a reminder's <i>internal</i> recurrence
/// (<c>RecurringCron</c> / interval presets in the registry): the scan just sweeps for what is due; per-reminder
/// timing lives in the DB (<c>NextOccurrenceAt</c>), never as a Quartz trigger.
/// </para>
/// </summary>
public sealed class ReminderScanOptions
{
    public const string SectionName = "ReminderScan";

    /// <summary>Scan cadence in minutes. Default 5. Ignored when <see cref="Cron"/> is set.</summary>
    public int CadenceMinutes { get; set; } = 5;

    /// <summary>Optional Quartz cron (UTC) overriding <see cref="CadenceMinutes"/> for an irregular cadence.</summary>
    public string? Cron { get; set; }

    /// <summary>
    /// The Quartz-level misfire policy for a missed scan window (process down, etc.). Default
    /// <see cref="MisfirePolicy.FireAndProceed"/>: on recovery, fire one scan now to catch up, then resume —
    /// the scan is idempotent + DB-deduped, so a single catch-up fire sweeps everything that came due. This is
    /// distinct from the <i>per-occurrence</i> catch-up the scan handler owns (it collapses older missed
    /// instants of one reminder to skip rows and sends only the most recent).
    /// </summary>
    public MisfirePolicy MisfirePolicy { get; set; } = MisfirePolicy.FireAndProceed;

    /// <summary>Translate the configured cadence into the Scheduler contract's <see cref="ScheduleSpec"/>.</summary>
    public ScheduleSpec ToScheduleSpec() =>
        string.IsNullOrWhiteSpace(Cron)
            ? ScheduleSpec.Every(JobIntervalPreset.Minute, CadenceMinutes)
            : ScheduleSpec.FromCron(Cron);
}