using AdhdTimeOrganizer.Scheduler.domain.entity;
using AdhdTimeOrganizer.Scheduler.domain.@enum;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MojaDigitalnaFirma.Kernel.scheduling;
using Quartz;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.domain.helper;
using Sydowwe.Framework.domain.result;
using Sydowwe.Framework.infrastructure.persistence;
using IScheduler = MojaDigitalnaFirma.Kernel.scheduling.IScheduler;

namespace AdhdTimeOrganizer.Scheduler.infrastructure;

/// <summary>
/// The Core.Scheduler implementation of the <see cref="Quartz.IScheduler"/> contract: it turns a
/// <see cref="Kernel.scheduling.RecurringJobRegistration"/> into a registry row + a live Quartz trigger pointing at the
/// single durable dispatcher job, idempotently keyed by <see cref="ScheduledJob.JobKey"/>.
/// <para>
/// Auto-registered via the <see cref="IScopedService"/> marker (like NotificationService) and safe to call
/// with <b>no authenticated user</b> — owners register from a startup hosted service or from a job, and the
/// registry entities derive from the plain table base (not the user-scoped one).
/// </para>
/// </summary>
public class SchedulerService(
    DbContext dbContext,
    ISchedulerFactory schedulerFactory,
    IEnumerable<IScheduledJobHandler> handlers,
    ILogger<SchedulerService> logger) : IScheduler, IScopedService
{
    /// <summary>How many times the registry upsert re-reads + retries when it loses a concurrency race to a fire.</summary>
    private const int MaxRegisterAttempts = 3;

    public async Task RegisterRecurringJobAsync(RecurringJobRegistration registration, CancellationToken ct)
    {
        Validate(registration);

        // 1. Build the trigger and (re)schedule it in place — all triggers point at the single dispatcher
        // job and carry the business JobKey in their JobDataMap. This only touches the Quartz RAM store
        // (not the DB), so it stays outside the upsert retry loop below.
        var scheduler = await schedulerFactory.GetScheduler(ct);
        var triggerKey = SchedulerQuartzConfig.TriggerKeyFor(registration.JobKey);
        var trigger = BuildTrigger(registration, triggerKey);

        var nextFire = await scheduler.CheckExists(triggerKey, ct)
            ? await scheduler.RescheduleJob(triggerKey, trigger, ct)
            : await scheduler.ScheduleJob(trigger, ct);

        // 2. Upsert the registry row by JobKey, retrying on a concurrency conflict. An interval trigger
        // whose first fire is immediate fires the instant it is scheduled above, so the running dispatcher
        // can rewrite this row's observability fields (LastRunAt/NextRunAt → a new xmin) concurrently with
        // this reconcile write. The dispatcher swallows its side of that race (the run log is the truth);
        // the reconcile re-reads the live token and retries.
        await UpsertRegistryRowWithRetryAsync(registration, nextFire?.UtcDateTime, ct);

        logger.LogInformation("Registered recurring job {JobKey} ({OwnerModule}); next run {NextRunAt:o}",
            registration.JobKey, registration.OwnerModule, nextFire?.UtcDateTime);
    }

    private async Task UpsertRegistryRowWithRetryAsync(RecurringJobRegistration registration, DateTime? nextRunAt, CancellationToken ct)
    {
        for (var attempt = 1;; attempt++)
        {
            var job = await dbContext.Set<ScheduledJob>().FirstOrDefaultAsync(x => x.JobKey == registration.JobKey, ct);
            var isNew = job is null;
            if (job is null)
                job = new ScheduledJob { JobKey = registration.JobKey, HandlerKey = registration.HandlerKey, OwnerModule = registration.OwnerModule };
            else
                // The tracked instance carries a stale row_version (the Postgres xmin concurrency token) when
                // either reconcile-on-boot re-registered inside the same DI scope, or — on a retry — a racing
                // fire bumped the token after our last read. Refresh it from the DB so the optimistic-
                // concurrency check compares against the live token.
                await dbContext.Entry(job).ReloadAsync(ct);

            ApplyRegistration(job, registration);

            // Persist NextRunAt from the trigger's computed first/next fire time, through the DbContextHelper
            // Result helpers for consistent exception→Result classification (CQ-3).
            job.NextRunAt = nextRunAt;
            var result = isNew ? await dbContext.AddEntityAsync(job, ct) : await dbContext.UpdateEntityAsync(job, ct);

            if (!result.Failed)
                return;

            // Only a lost concurrency race is retryable — re-read and reapply. Any other failure, or running
            // out of attempts, is fatal.
            if (result.ErrorType != ResultErrorType.DbConcurrencyError || attempt >= MaxRegisterAttempts)
                ThrowIfFailed(result, registration.JobKey);
        }
    }

    public async Task RemoveJobAsync(string jobKey, CancellationToken ct)
    {
        var scheduler = await schedulerFactory.GetScheduler(ct);
        await scheduler.UnscheduleJob(SchedulerQuartzConfig.TriggerKeyFor(jobKey), ct);

        var job = await dbContext.Set<ScheduledJob>().FirstOrDefaultAsync(x => x.JobKey == jobKey, ct);
        if (job is null)
            return; // idempotent: unknown / already-removed key is a no-op success.

        job.Status = JobStatus.Removed;
        job.NextRunAt = null;
        ThrowIfFailed(await dbContext.UpdateEntityAsync(job, ct), jobKey);
    }

    public async Task PauseJobAsync(string jobKey, CancellationToken ct)
    {
        var scheduler = await schedulerFactory.GetScheduler(ct);
        await scheduler.PauseTrigger(SchedulerQuartzConfig.TriggerKeyFor(jobKey), ct);

        var job = await dbContext.Set<ScheduledJob>().FirstOrDefaultAsync(x => x.JobKey == jobKey, ct);
        if (job is null)
            return;

        job.Status = JobStatus.Paused;
        job.NextRunAt = null;
        ThrowIfFailed(await dbContext.UpdateEntityAsync(job, ct), jobKey);
    }

    public async Task ResumeJobAsync(string jobKey, CancellationToken ct)
    {
        var scheduler = await schedulerFactory.GetScheduler(ct);
        var triggerKey = SchedulerQuartzConfig.TriggerKeyFor(jobKey);
        await scheduler.ResumeTrigger(triggerKey, ct);

        var job = await dbContext.Set<ScheduledJob>().FirstOrDefaultAsync(x => x.JobKey == jobKey, ct);
        if (job is null)
            return;

        job.Status = JobStatus.Active;
        var trigger = await scheduler.GetTrigger(triggerKey, ct);
        job.NextRunAt = trigger?.GetNextFireTimeUtc()?.UtcDateTime;
        ThrowIfFailed(await dbContext.UpdateEntityAsync(job, ct), jobKey);
    }

    public async Task TriggerNowAsync(string jobKey, CancellationToken ct)
    {
        // Fire the shared dispatcher once, off-schedule. Works even when the job's trigger is paused —
        // TriggerJob bypasses the trigger entirely. The data map tells the dispatcher (phase 03) which job
        // to run and that this is a Manual fire; the run row will record it.
        var scheduler = await schedulerFactory.GetScheduler(ct);
        var data = new JobDataMap
        {
            { SchedulerQuartzConfig.JobKeyDataKey, jobKey },
            { SchedulerQuartzConfig.TriggerSourceDataKey, nameof(TriggerSource.Manual) }
        };
        await scheduler.TriggerJob(SchedulerQuartzConfig.DispatcherJobKey, data, ct);
    }

    /// <summary>
    /// Surfaces a failed <see cref="DbContextHelper"/> save as a thrown exception, preserving the contract's
    /// fail-loud behavior while reusing <c>HandleException</c>'s exception→<see cref="Result"/> classification.
    /// </summary>
    private static void ThrowIfFailed(Result result, string jobKey)
    {
        if (result.Failed)
            throw new InvalidOperationException(
                $"Scheduler persistence failed for job '{jobKey}' ({result.ErrorType}): {result.ErrorMessage}");
    }

    /// <summary>Fail fast at registration: bad cron, a non-positive interval, or an unknown handler key.</summary>
    private void Validate(RecurringJobRegistration registration)
    {
        var spec = registration.Schedule;
        switch (spec.Type)
        {
            case JobScheduleType.Cron when string.IsNullOrWhiteSpace(spec.Cron) || !CronExpression.IsValidExpression(spec.Cron):
                throw new ArgumentException($"Invalid cron expression '{spec.Cron}' for job '{registration.JobKey}'.", nameof(registration));
            case JobScheduleType.Interval when spec.IntervalPreset is null:
                throw new ArgumentException($"Interval schedule for job '{registration.JobKey}' requires an interval preset.", nameof(registration));
            case JobScheduleType.Interval when spec.IntervalCount < 1:
                throw new ArgumentException($"Interval count must be positive for job '{registration.JobKey}'.", nameof(registration));
            case JobScheduleType.Once when spec.RunAtUtc is null:
                throw new ArgumentException($"One-shot schedule for job '{registration.JobKey}' requires a run-at instant.", nameof(registration));
        }

        if (registration.MaxRetries < 0)
            throw new ArgumentException($"MaxRetries must be non-negative for job '{registration.JobKey}'.", nameof(registration));

        if (spec.TimeZoneId is not null)
            try
            {
                _ = TimeZoneInfo.FindSystemTimeZoneById(spec.TimeZoneId);
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                throw new ArgumentException(
                    $"Unknown or invalid IANA time zone '{spec.TimeZoneId}' for job '{registration.JobKey}'.", nameof(registration), ex);
            }

        if (!ScheduledJobHandlerResolver.Exists(handlers, registration.HandlerKey))
            throw new ArgumentException($"No IScheduledJobHandler registered for handler key '{registration.HandlerKey}'.", nameof(registration));
    }

    /// <summary>Maps the registration onto the registry row, mirroring <see cref="ScheduleSpec"/> into the schedule columns.</summary>
    private static void ApplyRegistration(ScheduledJob job, RecurringJobRegistration registration)
    {
        var spec = registration.Schedule;
        var isInterval = spec.Type == JobScheduleType.Interval;

        job.HandlerKey = registration.HandlerKey;
        job.OwnerModule = registration.OwnerModule;
        job.Description = registration.Description;
        job.MisfirePolicy = registration.MisfirePolicy;
        job.DisallowConcurrent = registration.DisallowConcurrent;
        job.MaxRetries = registration.MaxRetries;
        job.AlertOnFailure = registration.AlertOnFailure;
        job.PayloadJson = registration.Payload is null ? null : JsonHelper.Serialize(registration.Payload);

        job.ScheduleType = spec.Type;
        job.Cron = spec.Type == JobScheduleType.Cron ? spec.Cron : null;
        job.IntervalPreset = isInterval ? spec.IntervalPreset : null;
        job.IntervalCount = isInterval ? spec.IntervalCount : null;
        job.RunAtUtc = spec.Type == JobScheduleType.Once ? spec.RunAtUtc : null;
        job.TimeZoneId = spec.TimeZoneId;

        // Re-registration reconciles to Active (a RAM-store restart wipes triggers, so reconciliation
        // recreates them; a prior pause does not survive a restart by design — see the module doc).
        job.Status = JobStatus.Active;
    }

    private static ITrigger BuildTrigger(RecurringJobRegistration registration, TriggerKey triggerKey)
    {
        var builder = TriggerBuilder.Create()
            .WithIdentity(triggerKey)
            .ForJob(SchedulerQuartzConfig.DispatcherJobKey)
            .UsingJobData(SchedulerQuartzConfig.JobKeyDataKey, registration.JobKey);

        return ApplySchedule(builder, registration.Schedule, registration.MisfirePolicy).Build();
    }

    /// <summary>
    /// Applies the cron/interval schedule + misfire policy to <paramref name="builder"/>, anchoring every
    /// wall-clock schedule (cron + day-and-larger calendar intervals) to the spec's
    /// <see cref="ScheduleSpec.TimeZoneId"/> (null ⇒ UTC). Split out from <see cref="BuildTrigger"/> so the
    /// DST test can supply its own <c>StartAt</c> and compute fire times across a transition without a scheduler.
    /// </summary>
    internal static TriggerBuilder ApplySchedule(TriggerBuilder builder, ScheduleSpec spec, MisfirePolicy policy)
    {
        var timeZone = ResolveTimeZone(spec.TimeZoneId);
        return spec.Type switch
        {
            JobScheduleType.Cron => builder.WithCronSchedule(spec.Cron!, x => ApplyCronMisfire(x.InTimeZone(timeZone), policy)),
            JobScheduleType.Once => ApplyOnce(builder, spec.RunAtUtc!.Value),
            _ => ApplyInterval(builder, spec, policy, timeZone)
        };
    }

    /// <summary>
    /// A one-shot: a single non-recurring fire at the fixed UTC instant. Carries the instant on the trigger
    /// (<see cref="SchedulerQuartzConfig.RunOnceAtDataKey"/>) so the dispatcher treats it as the scheduled fire
    /// time — that is what makes a boot re-registration re-fire recognisable as a duplicate occurrence by the
    /// run-log dedup (which survives restart, unlike the RAM-store trigger). A <b>past-due</b> instant fires
    /// once immediately via the misfire path (<c>FireNow</c>) rather than being silently dropped; if it already
    /// ran, that immediate fire is deduped, so the effect is at-most-once.
    /// </summary>
    private static TriggerBuilder ApplyOnce(TriggerBuilder builder, DateTime runAtUtc)
    {
        return builder
            .StartAt(new DateTimeOffset(runAtUtc, TimeSpan.Zero))
            .WithSimpleSchedule(x => x.WithRepeatCount(0).WithMisfireHandlingInstructionFireNow())
            .UsingJobData(SchedulerQuartzConfig.RunOnceAtDataKey, runAtUtc.Ticks);
    }

    /// <summary>Resolves the spec's IANA time-zone id (null ⇒ UTC). Throws for an unknown/invalid id (validated up front).</summary>
    internal static TimeZoneInfo ResolveTimeZone(string? timeZoneId) => timeZoneId is null ? TimeZoneInfo.Utc : TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);

    /// <summary>
    /// Picks the Quartz schedule that matches the interval <i>unit</i>: sub-day units are fixed-length, so a
    /// SimpleSchedule is correct (and time-zone-agnostic); day/week/month/quarter/year are variable-length (DST,
    /// month/quarter/year boundaries) and must use a CalendarIntervalSchedule anchored to <paramref name="timeZone"/>
    /// — never a fixed-millisecond approximation. Quarter is expressed as 3 months.
    /// </summary>
    private static TriggerBuilder ApplyInterval(TriggerBuilder builder, ScheduleSpec spec, MisfirePolicy policy, TimeZoneInfo timeZone)
    {
        var count = spec.IntervalCount;
        return spec.IntervalPreset!.Value switch
        {
            JobIntervalPreset.Minute => builder.WithSimpleSchedule(x => ApplySimpleMisfire(x.WithIntervalInMinutes(count).RepeatForever(), policy)),
            JobIntervalPreset.Hour => builder.WithSimpleSchedule(x => ApplySimpleMisfire(x.WithIntervalInHours(count).RepeatForever(), policy)),
            JobIntervalPreset.Day => builder.WithCalendarIntervalSchedule(x => ApplyCalendarMisfire(Anchor(x.WithIntervalInDays(count), timeZone), policy)),
            JobIntervalPreset.Week => builder.WithCalendarIntervalSchedule(x => ApplyCalendarMisfire(Anchor(x.WithIntervalInWeeks(count), timeZone), policy)),
            JobIntervalPreset.Month => builder.WithCalendarIntervalSchedule(x => ApplyCalendarMisfire(Anchor(x.WithIntervalInMonths(count), timeZone), policy)),
            JobIntervalPreset.Quarter => builder.WithCalendarIntervalSchedule(x => ApplyCalendarMisfire(Anchor(x.WithIntervalInMonths(count * 3), timeZone), policy)),
            JobIntervalPreset.Year => builder.WithCalendarIntervalSchedule(x => ApplyCalendarMisfire(Anchor(x.WithIntervalInYears(count), timeZone), policy)),
            _ => throw new ArgumentOutOfRangeException(nameof(spec))
        };
    }

    /// <summary>
    /// Anchors a calendar interval to <paramref name="timeZone"/>'s wall clock. <c>InTimeZone</c> alone is
    /// <b>not</b> enough: a Quartz CalendarIntervalTrigger otherwise advances by a fixed elapsed span, so its
    /// hour-of-day drifts by the DST offset across a transition. <c>PreserveHourOfDayAcrossDaylightSavings</c>
    /// re-anchors each fire to the original local hour — the behaviour a "run at 03:00 local" or a
    /// calendar-boundary (midnight-of-the-1st) job expects. Harmless for a UTC zone (no DST to preserve).
    /// </summary>
    private static CalendarIntervalScheduleBuilder Anchor(CalendarIntervalScheduleBuilder builder, TimeZoneInfo timeZone) => builder.InTimeZone(timeZone).PreserveHourOfDayAcrossDaylightSavings(true);

    private static void ApplyCronMisfire(CronScheduleBuilder builder, MisfirePolicy policy)
    {
        switch (policy)
        {
            case MisfirePolicy.FireAndProceed:
                builder.WithMisfireHandlingInstructionFireAndProceed();
                break;
            case MisfirePolicy.IgnoreMisfires:
                builder.WithMisfireHandlingInstructionIgnoreMisfires();
                break;
            case MisfirePolicy.DoNothing:
                builder.WithMisfireHandlingInstructionDoNothing();
                break;
        }
    }

    private static void ApplyCalendarMisfire(CalendarIntervalScheduleBuilder builder, MisfirePolicy policy)
    {
        switch (policy)
        {
            case MisfirePolicy.FireAndProceed:
                builder.WithMisfireHandlingInstructionFireAndProceed();
                break;
            case MisfirePolicy.IgnoreMisfires:
                builder.WithMisfireHandlingInstructionIgnoreMisfires();
                break;
            case MisfirePolicy.DoNothing:
                builder.WithMisfireHandlingInstructionDoNothing();
                break;
        }
    }

    /// <summary>
    /// SimpleSchedule has no FireAndProceed/DoNothing names: FireAndProceed → FireNow (catch up once now),
    /// DoNothing → NextWithRemainingCount (skip to the next scheduled slot).
    /// </summary>
    private static void ApplySimpleMisfire(SimpleScheduleBuilder builder, MisfirePolicy policy)
    {
        switch (policy)
        {
            case MisfirePolicy.FireAndProceed:
                builder.WithMisfireHandlingInstructionFireNow();
                break;
            case MisfirePolicy.IgnoreMisfires:
                builder.WithMisfireHandlingInstructionIgnoreMisfires();
                break;
            case MisfirePolicy.DoNothing:
                builder.WithMisfireHandlingInstructionNextWithRemainingCount();
                break;
        }
    }
}