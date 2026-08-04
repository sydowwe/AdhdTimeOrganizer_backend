using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.domain.service;
using AdhdTimeOrganizer.domain.serviceContract;
using AdhdTimeOrganizer.infrastructure.persistence;
using Microsoft.EntityFrameworkCore;
using Quartz;

namespace AdhdTimeOrganizer.infrastructure.jobs;

[DisallowConcurrentExecution]
public class RoutineTodoListResetJob(IServiceScopeFactory scopeFactory, ILogger<RoutineTodoListResetJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        logger.LogInformation("RoutineTodoListResetJob started");

        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var notifier = scope.ServiceProvider.GetRequiredService<IRoutinePeriodNotificationService>();

        var periods = await dbContext.Set<RoutineTimePeriod>()
            .Include(tp => tp.RoutineTodoListColl)
            .ToListAsync(context.CancellationToken);

        var now = DateTime.UtcNow;
        var totalReset = 0;
        var completionRecords = new List<RoutinePeriodCompletion>();
        var reset = new List<(RoutineTimePeriod Period, RoutineResetService.RoutinePeriodReset Result)>();

        foreach (var period in periods)
        {
            var items = period.RoutineTodoListColl.ToList();
            RoutineResetService.CheckGrace(period, now);
            var result = RoutineResetService.TryReset(period, items, now);
            if (result is { } r)
            {
                completionRecords.Add(r.Completion);
                reset.Add((period, r));
                totalReset += items.Count;
            }
        }

        if (reset.Count == 0)
        {
            logger.LogInformation("No items to reset");
            return;
        }

        dbContext.Set<RoutinePeriodCompletion>().AddRange(completionRecords);
        await dbContext.SaveChangesAsync(context.CancellationToken);
        logger.LogInformation("Reset {Count} routine todo list items", totalReset);

        // After the commit, deliberately: the summary announces a reset that has happened, and a notification
        // failure must not be able to take the reset down with it (the notifier swallows, but the ordering is
        // the part that makes that safe).
        foreach (var (period, result) in reset)
            await notifier.NotifyPeriodEndedAsync(period, result.Completion, result.Outcome, context.CancellationToken);
    }
}