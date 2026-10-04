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

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        try
        {
            var scheduler = scope.ServiceProvider.GetRequiredService<IScheduler>();
            await scheduler.RegisterRecurringJobAsync(CompleteFinishedTasksRegistration, cancellationToken);
        }
        catch (Exception ex)
        {
            // Don't block host startup — rest blocks simply stay open and the job shows as absent in the
            // dashboard. JobKey is non-PII.
            logger.LogError(ex, "Failed to register the planning recurring job {JobKey}",
                CompleteFinishedTasksRegistration.JobKey);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
