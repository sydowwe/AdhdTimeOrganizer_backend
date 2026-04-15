using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.domain.service;
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

        var periods = await dbContext.Set<RoutineTimePeriod>()
            .Include(tp => tp.RoutineTodoListColl)
            .ToListAsync(context.CancellationToken);

        var now = DateTime.UtcNow;
        var totalReset = 0;
        var completionRecords = new List<RoutinePeriodCompletion>();

        foreach (var period in periods)
        {
            var items = period.RoutineTodoListColl.ToList();
            RoutineResetService.CheckGrace(period, now);
            var completion = RoutineResetService.TryReset(period, items, now);
            if (completion != null)
            {
                completionRecords.Add(completion);
                totalReset += items.Count;
            }
        }

        if (totalReset == 0)
        {
            logger.LogInformation("No items to reset");
            return;
        }

        dbContext.Set<RoutinePeriodCompletion>().AddRange(completionRecords);
        await dbContext.SaveChangesAsync(context.CancellationToken);
        logger.LogInformation("Reset {Count} routine todo list items", totalReset);
    }
}
