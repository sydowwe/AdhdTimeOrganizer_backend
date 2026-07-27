namespace MojaDigitalnaFirma.Kernel.scheduling;

/// <summary>
/// How a job's schedule is expressed: a fixed interval ("every N of a unit"), a cron expression, or a
/// one-shot that fires exactly once at a fixed instant (<see cref="RunOnceAt"/>). Most jobs recur; the
/// one-shot is the exception (the type name is kept for source-compat). Construct via
/// <see cref="FromCron"/> / <see cref="Every"/> / <see cref="RunOnceAt"/> rather than the initializer.
/// <para>
/// <b>Time zone.</b> A schedule anchored to a wall-clock time (a cron "run at 03:00", or a
/// day/week/month/quarter/year calendar interval whose fire instant tracks local midnight) is interpreted
/// in <see cref="TimeZoneId"/>. When <see cref="TimeZoneId"/> is <c>null</c> (the default) the schedule is
/// interpreted in <b>UTC</b> — the historical behaviour, kept as the default so an existing registration's
/// effective wall-clock fire instant does not change unless its owner explicitly opts in. Owners that mean
/// Slovak local time (calendar-boundary rollovers, "run at 03:00 local") pass an IANA id such as
/// <c>"Europe/Bratislava"</c>, which then holds the wall-clock across DST transitions. Sub-day intervals
/// (<see cref="JobIntervalPreset.Minute"/> / <see cref="JobIntervalPreset.Hour"/>) are fixed-length and
/// therefore time-zone-agnostic — <see cref="TimeZoneId"/> has no effect on them.
/// </para>
/// </summary>
public sealed class ScheduleSpec
{
    public JobScheduleType Type { get; private init; }

    /// <summary>Cron expression (Quartz syntax). Set only when <see cref="Type"/> is <see cref="JobScheduleType.Cron"/>.</summary>
    public string? Cron { get; private init; }

    /// <summary>Interval unit. Set only when <see cref="Type"/> is <see cref="JobScheduleType.Interval"/>.</summary>
    public JobIntervalPreset? IntervalPreset { get; private init; }

    /// <summary>Number of <see cref="JobIntervalPreset"/> units between fires (e.g. 5 → "every 5 minutes").</summary>
    public int IntervalCount { get; private init; } = 1;

    /// <summary>
    /// The single UTC instant a one-shot fires at. Set only when <see cref="Type"/> is
    /// <see cref="JobScheduleType.Once"/>. A one-shot is time-zone-agnostic (it is an absolute instant), so
    /// <see cref="TimeZoneId"/> does not apply. A past-due one-shot fires once on (re)registration via the
    /// misfire path; a boot re-registration of an already-run one-shot is recognised as a duplicate of this
    /// instant by the dispatcher's occurrence dedup and does not fire again (see <see cref="RunOnceAt"/>).
    /// </summary>
    public DateTime? RunAtUtc { get; private init; }

    /// <summary>
    /// Optional IANA time-zone id (e.g. <c>"Europe/Bratislava"</c>) the wall-clock schedule is anchored to.
    /// <c>null</c> ⇒ UTC (the default; preserves the fire instant of pre-existing registrations). Resolved
    /// via <see cref="System.TimeZoneInfo.FindSystemTimeZoneById"/> — an unknown id fails fast at registration.
    /// </summary>
    public string? TimeZoneId { get; private init; }

    private ScheduleSpec()
    {
    }

    /// <summary>
    /// A cron schedule (Quartz syntax). Interpreted in <paramref name="timeZoneId"/> when supplied
    /// (an IANA id such as <c>"Europe/Bratislava"</c>), otherwise in UTC.
    /// </summary>
    public static ScheduleSpec FromCron(string cron, string? timeZoneId = null) => new() { Type = JobScheduleType.Cron, Cron = cron, TimeZoneId = timeZoneId };

    /// <summary>
    /// An interval schedule firing every <paramref name="count"/> <paramref name="preset"/> units.
    /// Day-and-larger intervals track the wall clock in <paramref name="timeZoneId"/> when supplied
    /// (IANA id, e.g. <c>"Europe/Bratislava"</c>), otherwise UTC; sub-day intervals ignore it (fixed-length).
    /// </summary>
    public static ScheduleSpec Every(JobIntervalPreset preset, int count = 1, string? timeZoneId = null) =>
        new() { Type = JobScheduleType.Interval, IntervalPreset = preset, IntervalCount = count, TimeZoneId = timeZoneId };

    /// <summary>
    /// A one-shot schedule firing exactly once at <paramref name="runAt"/> (an absolute instant; a
    /// <see cref="DateTimeKind.Local"/> value is converted to UTC, an <see cref="DateTimeKind.Unspecified"/>
    /// one is taken as UTC). A future instant fires at that time; a past-due instant fires once immediately
    /// via the misfire path. Re-registering the same one-shot after it has run does <b>not</b> fire it again —
    /// the dispatcher treats the fire as scheduled at <paramref name="runAt"/> and its occurrence dedup (which
    /// survives restarts through the run log) recognises the re-fire as a duplicate.
    /// </summary>
    public static ScheduleSpec RunOnceAt(DateTime runAt) =>
        new()
        {
            Type = JobScheduleType.Once,
            RunAtUtc = runAt.Kind == DateTimeKind.Local ? runAt.ToUniversalTime() : DateTime.SpecifyKind(runAt, DateTimeKind.Utc)
        };
}