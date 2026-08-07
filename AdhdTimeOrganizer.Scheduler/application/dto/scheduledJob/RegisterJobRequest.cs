using Sydowwe.Framework.Contracts.scheduling;

namespace AdhdTimeOrganizer.Scheduler.application.dto.scheduledJob;

/// <summary>
/// Flat request for the diagnostic register endpoint — mapped to a <see cref="RecurringJobRegistration"/>
/// (the typed <see cref="ScheduleSpec"/> is rebuilt from <see cref="JobScheduleType"/> + the cron/interval
/// fields). Validation (cron / interval / handler key) happens in the scheduler, not here.
/// </summary>
public record RegisterJobRequest
{
    public required string JobKey { get; init; }
    public required string HandlerKey { get; init; }
    public required string OwnerModule { get; init; }
    public string? Description { get; init; }

    public required JobScheduleType ScheduleType { get; init; }
    public string? Cron { get; init; }
    public JobIntervalPreset? IntervalPreset { get; init; }
    public int IntervalCount { get; init; } = 1;

    /// <summary>Optional IANA zone (e.g. <c>"Europe/Bratislava"</c>) the wall-clock schedule is anchored to; null ⇒ UTC.</summary>
    public string? TimeZoneId { get; init; }

    public MisfirePolicy MisfirePolicy { get; init; } = MisfirePolicy.FireAndProceed;
    public bool DisallowConcurrent { get; init; }

    /// <summary>Whether a terminal failure alerts through <see cref="IJobFailureNotifier"/>. On by default.</summary>
    public bool AlertOnFailure { get; init; } = true;

    public object? Payload { get; init; }

    /// <summary>
    /// Builds the typed registration. An interval request must carry an <see cref="IntervalPreset"/>
    /// (the endpoint rejects a missing one before calling this); cron validity / count / handler key are
    /// validated by the scheduler.
    /// </summary>
    public RecurringJobRegistration ToRegistration() =>
        new()
        {
            JobKey = JobKey,
            HandlerKey = HandlerKey,
            OwnerModule = OwnerModule,
            Description = Description,
            Schedule = ScheduleType == JobScheduleType.Cron
                ? ScheduleSpec.FromCron(Cron ?? string.Empty, TimeZoneId)
                : ScheduleSpec.Every(IntervalPreset!.Value, IntervalCount, TimeZoneId),
            MisfirePolicy = MisfirePolicy,
            DisallowConcurrent = DisallowConcurrent,
            AlertOnFailure = AlertOnFailure,
            Payload = Payload
        };
}