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

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var allItems = await dbContext.RoutineTodoLists
            .Include(rtl => rtl.RoutineTimePeriod)
            .ToListAsync(context.CancellationToken);

        var toReset = allItems
            .Where(rtl =>
            {
                var lastReset = rtl.LastResetDate ?? DateOnly.FromDateTime(rtl.CreatedTimestamp);
                return lastReset.AddDays(rtl.RoutineTimePeriod.LengthInDays) <= today;
            })
            .ToList();

        if (toReset.Count == 0)
        {
            logger.LogInformation("No items to reset today");
            return;
        }

        foreach (var item in toReset)
        {
            item.IsDone = false;
            item.DoneCount = null;
            item.LastResetDate = today;
        }

        await dbContext.SaveChangesAsync(context.CancellationToken);
        logger.LogInformation("Reset {Count} routine todo list items", toReset.Count);
    }
}
