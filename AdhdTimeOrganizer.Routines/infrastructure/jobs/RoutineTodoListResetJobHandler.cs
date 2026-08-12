using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using AdhdTimeOrganizer.Routines.domain.service;
using AdhdTimeOrganizer.Routines.domain.serviceContract;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.Contracts.scheduling;

namespace AdhdTimeOrganizer.Routines.infrastructure.jobs;

/// <summary>
/// Daily reset of the routine to-do lists whose period has rolled over.
/// <para>
/// A keyed <see cref="IScheduledJobHandler"/>, not a Quartz <c>IJob</c>: this slice owns the work, the
/// Scheduler module owns the substrate (trigger, run log, retry, failure alerting). The schedule is
/// pushed on boot by <see cref="scheduling.RoutinesScheduledJobsRegistrar"/>.
/// </para>
/// <para>
/// Runs unauthenticated (background context), which is exactly when the host's per-user query filter is
/// inert — so every user's periods are in scope, as a sweep requires.
/// </para>
/// </summary>
public class RoutineTodoListResetJobHandler(
    DbContext dbContext,
    IRoutinePeriodNotificationService notifier,
    ILogger<RoutineTodoListResetJobHandler> logger) : IScheduledJobHandler, IScopedService
{
    public const string HandlerKey = "Routines.TodoListReset";

    public string Key => HandlerKey;

    public async Task ExecuteAsync(ScheduledJobContext context, CancellationToken ct)
    {
        logger.LogInformation("RoutineTodoListResetJob started");

        var periods = await dbContext.Set<RoutineTimePeriod>()
            .Include(tp => tp.RoutineTodoListColl)
            .ThenInclude(t => t.Steps)
            .ToListAsync(ct);

        var now = DateTime.UtcNow;
        var totalReset = 0;
        var graceChanged = false;
        var completionRecords = new List<RoutinePeriodCompletion>();
        var reset = new List<(RoutineTimePeriod Period, RoutineResetService.RoutinePeriodReset Result)>();

        foreach (var period in periods)
        {
            var items = period.RoutineTodoListColl.ToList();
            graceChanged |= RoutineResetService.CheckGrace(period, now);
            var result = RoutineResetService.TryReset(period, items, now);
            if (result is { } r)
            {
                completionRecords.Add(r.Completion);
                reset.Add((period, r));
                totalReset += items.Count;
            }
        }

        if (reset.Count == 0 && !graceChanged)
        {
            logger.LogInformation("No items to reset");
            return;
        }

        dbContext.Set<RoutinePeriodCompletion>().AddRange(completionRecords);
        await dbContext.SaveChangesAsync(ct);
        logger.LogInformation("Reset {Count} routine todo list items", totalReset);

        // After the commit, deliberately: the summary announces a reset that has happened, and a notification
        // failure must not be able to take the reset down with it (the notifier swallows, but the ordering is
        // the part that makes that safe).
        foreach (var (period, result) in reset)
            await notifier.NotifyPeriodEndedAsync(period, result.Completion, result.Outcome, ct);
    }
}
