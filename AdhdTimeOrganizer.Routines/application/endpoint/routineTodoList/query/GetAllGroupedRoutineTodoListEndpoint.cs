using AdhdTimeOrganizer.Routines.application.dto.response.todoList;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using AdhdTimeOrganizer.Routines.domain.service;
using AdhdTimeOrganizer.Routines.domain.serviceContract;
using FastEndpoints;
using Sydowwe.Framework.application.extensions;

namespace AdhdTimeOrganizer.Routines.application.endpoint.todoList.routineTodoList.query;

public class GetAllGroupedRoutineTodoListEndpoint(
    DbContext dbContext,
    IRoutinePeriodNotificationService notifier) : EndpointWithoutRequest<IEnumerable<RoutineTodoListGroupedResponse>>
{
    public override void Configure()
    {
        Get("/routine-todo-list/grouped-by-time-period");

        Summary(s =>
        {
            s.Summary = "Get all routine todo lists grouped by time period";
            s.Description = "Retrieves all routine todo lists grouped by their time period";
            s.Response<IEnumerable<RoutineTodoListGroupedResponse>>(200, "Success");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var loggedUserId = User.GetId();
        var now = DateTime.UtcNow;

        var periods = await dbContext.Set<RoutineTimePeriod>()
            .Where(x => x.UserId == loggedUserId)
            .Include(tp => tp.RoutineTodoListColl)
            .ThenInclude(rtl => rtl.Activity)
            .ThenInclude(a => a.Role)
            .Include(tp => tp.RoutineTodoListColl)
            .ThenInclude(rtl => rtl.Activity)
            .ThenInclude(a => a.Category)
            .Include(tp => tp.RoutineTodoListColl)
            .ThenInclude(rtl => rtl.Steps)
            .ToListAsync(ct);

        var changed = false;
        var newCompletions = new List<RoutinePeriodCompletion>();
        var reset = new List<(RoutineTimePeriod Period, RoutineResetService.RoutinePeriodReset Result)>();
        foreach (var period in periods)
        {
            var items = period.RoutineTodoListColl.ToList();
            changed |= RoutineResetService.CheckGrace(period, now);
            // No job refills the freeze budget — the refill is lazy, so the surface that renders the budget is
            // the one that has to bring it current. Skipping it here would serve a stale count and refuse a
            // freeze the user is owed.
            changed |= RoutineStreakFreezeService.RefreshFreezeBudget(period, now);
            var result = RoutineResetService.TryReset(period, items, now);
            if (result is { } r)
            {
                changed = true;
                newCompletions.Add(r.Completion);
                reset.Add((period, r));
            }
        }

        if (changed)
        {
            dbContext.Set<RoutinePeriodCompletion>().AddRange(newCompletions);
            await dbContext.SaveChangesAsync(ct);
        }

        // This read applies resets lazily, so it is a real reset site and owes the same summary the nightly job
        // sends — whichever path reaches the elapsed period first is the one that announces it. Raised after the
        // commit and best-effort, so it cannot fail the request the user is waiting on.
        foreach (var (period, result) in reset)
            await notifier.NotifyPeriodEndedAsync(period, result.Completion, result.Outcome, ct);

        var periodIds = periods.Select(p => p.Id).ToList();
        var completions = await dbContext.Set<RoutinePeriodCompletion>()
            .Where(c => periodIds.Contains(c.TimePeriodId))
            .OrderBy(c => c.TimePeriodId)
            .ThenByDescending(c => c.PeriodStart)
            .ToListAsync(ct);

        var completionsByPeriod = completions
            .GroupBy(c => c.TimePeriodId)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var depth = periods.First(p => p.Id == g.Key).HistoryDepth;
                    return g.Take(depth).Reverse().ToList();
                }
            );

        var data = periods
            .Select(tp => new RoutineTodoListGroupedResponse
            {
                RoutineTimePeriod = RoutineTimePeriodResponse.From(
                    tp,
                    completionsByPeriod.GetValueOrDefault(tp.Id, []),
                    now),
                Items = tp.RoutineTodoListColl
                    .OrderBy(e => e.IsDone).ThenBy(e => e.DisplayOrder)
                    .Select(e => RoutineTodoListResponse.Projection(new[] { e }.AsQueryable()).Single())
                    .ToList()
            })
            .ToList();

        await Send.OkAsync(data, ct);
    }
}