using AdhdTimeOrganizer.Planning.infrastructure.jobs;
using Sydowwe.Framework.Contracts.scheduling;

namespace AdhdTimeOrganizer.Planning.infrastructure.scheduling;

/// <summary>
/// Boot reconciliation for the Planning slice's recurring jobs, pushed onto the Scheduler module through the
/// <see cref="IScheduler"/> contract — the same owner-pushes-its-schedule shape every other slice uses, so this
/// slice references no Quartz.
/// <para>
/// <b>Required on every boot:</b> the registration is an idempotent upsert by <c>JobKey</c>, and the Scheduler's
/// RAM job store drops all triggers on restart. A registrar dropped from <c>Program.cs</c> is silent — the job
/// simply never fires; <c>ModuleWiringTests.BootRegistrars_RegisterBothModulesRecurringJobs</c> is the guard.
/// </para>
/// </summary>
public sealed class PlanningScheduledJobsRegistrar(
    IServiceProvider services,
    ILogger<PlanningScheduledJobsRegistrar> logger) : IHostedService
{
    public static RecurringJobRegistration CompleteFinishedTasksRegistration { get; } = new()
    {
        JobKey = CompleteFinishedPlannerTasksJobHandler.HandlerKey,
        HandlerKey = CompleteFinishedPlannerTasksJobHandler.HandlerKey,
        OwnerModule = "Planning",
        // Every five minutes: how late a rest block may close. The scan is a partial index over a handful of rows.
        Schedule = ScheduleSpec.FromCron("0 0/5 * * * ?"),
        DisallowConcurrent = true,
        Description = "Completes in-progress planner tasks marked auto-complete once their slot has ended."
    };

    public static RecurringJobRegistration MaterialiseRepeatingTasksRegistration { get; } = new()
    {
        JobKey = MaterialiseRepeatingTasksJobHandler.HandlerKey,
        HandlerKey = MaterialiseRepeatingTasksJobHandler.HandlerKey,
        OwnerModule = "Planning",
        // Every 15 minutes in UTC, deliberately with no zone: the handler is zone-aware per user, and the quarter
        // hour is the granularity of every zone's midnight. A user is due once per local day; the rest of the ticks
        // are one indexed read of the active auto-placing rules.
        Schedule = ScheduleSpec.FromCron("0 0/15 * * * ?"),
        DisallowConcurrent = true,
        Description = "Places auto-placing repeating rules' occurrences for each user's today and tomorrow, and their automatic reminders."
    };

    public static RecurringJobRegistration SlipNudgeRegistration { get; } = new()
    {
        JobKey = PlannerSlipNudgeJobHandler.HandlerKey,
        HandlerKey = PlannerSlipNudgeJobHandler.HandlerKey,
        OwnerModule = "Planning",
        // Every five minutes in UTC; the handler is zone-aware per user. Five is how late a "missed" or "time's up"
        // may arrive and still be about now — the overrun already waits five minutes past the end on purpose.
        Schedule = ScheduleSpec.FromCron("0 0/5 * * * ?"),
        DisallowConcurrent = true,
        Description = "Tells users when today's planned blocks slipped by unstarted, or a started block ran past its end."
    };

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var scheduler = scope.ServiceProvider.GetRequiredService<IScheduler>();

        foreach (var registration in new[] { CompleteFinishedTasksRegistration, MaterialiseRepeatingTasksRegistration, SlipNudgeRegistration })
        {
            try
            {
                await scheduler.RegisterRecurringJobAsync(registration, cancellationToken);
            }
            catch (Exception ex)
            {
                // Don't block host startup — the job shows as absent in the dashboard, and the work it does falls
                // back to the user's own reads (rest blocks stay open; occurrences wait for a day-plan read).
                // JobKey is non-PII.
                logger.LogError(ex, "Failed to register the planning recurring job {JobKey}", registration.JobKey);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
