using MojaDigitalnaFirma.Kernel.scheduling;

namespace AdhdTimeOrganizer.Scheduler.application.job;

/// <summary>
/// Cadence and policy for the active overdue sweep (<see cref="OverdueJobSweepJobHandler"/>, scheduler
/// follow-up 08). Bound from the "OverdueJobSweep" config section; the defaults work out of the box with no
/// configuration, and every value here is deployment-tunable precisely because they are noise/latency
/// trade-offs rather than correctness.
/// </summary>
public sealed class OverdueJobSweepOptions
{
    public const string SectionName = "OverdueJobSweep";

    /// <summary>
    /// Kill switch. <c>false</c> keeps the sweep job registered and firing but makes it a no-op — so an
    /// operator drowning in overdue alerts can silence them from configuration without a deploy, and the
    /// pull-only health view still shows the same jobs.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Sweep cadence in minutes. Default 10. Ignored when <see cref="Cron"/> is set.
    /// <para>
    /// The cadence is only the <i>detection latency</i> floor, not the alert frequency — the throttle
    /// (<see cref="AlertThrottleHours"/>) decides how often a still-broken job re-alerts, so this can stay
    /// simple and frequent. It must be shorter than <see cref="AlertMarginMinutes"/> is long, or a job could
    /// cross the margin and recover between two ticks and never be seen.
    /// </para>
    /// </summary>
    public int CadenceMinutes { get; set; } = 10;

    /// <summary>Optional Quartz cron (UTC) overriding <see cref="CadenceMinutes"/> for an irregular cadence.</summary>
    public string? Cron { get; set; }

    /// <summary>
    /// How far past its <c>NextRunAt</c> an <c>Active</c> job must be before the sweep <b>alerts</b>. Default
    /// 5 minutes.
    /// <para>
    /// <b>This is a skew cushion, not the defence against long-running jobs.</b> That used to be its job — a
    /// running handler holds a stale <c>NextRunAt</c> until it returns, so the margin had to exceed the
    /// slowest job body, which traded slow detection for a guess that broke the moment a job got slower. The
    /// dispatcher's <c>ScheduledJob.RunningSince</c> marker now answers that exactly, so all this has to absorb
    /// is the gap between a trigger firing and the marker being written, plus clock skew. Hence 5 minutes
    /// rather than a defensive 15.
    /// </para>
    /// </summary>
    public int AlertMarginMinutes { get; set; } = 5;

    /// <summary>
    /// How long a run may plausibly take before its <c>RunningSince</c> marker is treated as <b>stale</b> and
    /// ignored. Default 6 hours.
    /// <para>
    /// This bounds the one failure the marker itself cannot handle: a process killed mid-run leaves the marker
    /// set with nobody to clear it. Unbounded, that job would be permanently invisible to the sweep — a
    /// <b>false negative</b>, which for a detector is worse than the false positive the marker exists to
    /// prevent. Past this bound the job becomes alertable again, so the condition self-heals with no boot-time
    /// cleanup pass (which would have to race the scheduler starting).
    /// </para>
    /// <para>
    /// Set it above the slowest job body you expect and well below "how long am I willing to stay blind" —
    /// generously wide is fine, because the marker does the real work and this only catches crashes.
    /// </para>
    /// </summary>
    public int MaxRunHours { get; set; } = 6;

    /// <summary>
    /// Minimum gap between two overdue alerts for the same job. Default 12 hours — much longer than the
    /// failure throttle's 1 hour, because the two conditions decay differently: a failing job re-alerts only
    /// when it re-fires, whereas an overdue job is overdue <i>continuously</i> until a human fixes it, so an
    /// hourly repeat would mean 24 identical emails a day per Admin about a problem they already know about.
    /// Twice a day keeps it visible without becoming background noise.
    /// </summary>
    public int AlertThrottleHours { get; set; } = 12;

    public TimeSpan AlertMargin => TimeSpan.FromMinutes(AlertMarginMinutes);

    public TimeSpan MaxRunDuration => TimeSpan.FromHours(MaxRunHours);

    public TimeSpan AlertThrottleWindow => TimeSpan.FromHours(AlertThrottleHours);

    /// <summary>Translate the configured cadence into the Scheduler contract's <see cref="ScheduleSpec"/>.</summary>
    public ScheduleSpec ToScheduleSpec() =>
        string.IsNullOrWhiteSpace(Cron)
            ? ScheduleSpec.Every(JobIntervalPreset.Minute, CadenceMinutes)
            : ScheduleSpec.FromCron(Cron);
}