using AdhdTimeOrganizer.application.dto.response.toDoList;
using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.application.helper;
using AdhdTimeOrganizer.application.mapper.activityPlanning;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.domain.service;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using RoutineTodoListMapper = AdhdTimeOrganizer.application.mapper.todoList.RoutineTodoListMapper;

namespace AdhdTimeOrganizer.application.endpoint.todoList.routineTodoList.query;

public class GetAllGroupedRoutineTodoListEndpoint(
    AppDbContext dbContext,
    RoutineTimePeriodMapper routineTimePeriodMapper,
    RoutineTodoListMapper mapper) : EndpointWithoutRequest<IEnumerable<RoutineTodoListGroupedResponse>>
{
    public override void Configure()
    {
        Get("/routine-todo-list/grouped-by-time-period");
        Roles(EndpointHelper.GetUserOrHigherRoles());

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
        foreach (var period in periods)
        {
            var items = period.RoutineTodoListColl.ToList();
            changed |= RoutineResetService.CheckGrace(period, now);
            var completion = RoutineResetService.TryReset(period, items, now);
            if (completion != null)
            {
                changed = true;
                newCompletions.Add(completion);
            }
        }

        if (changed)
        {
            dbContext.Set<RoutinePeriodCompletion>().AddRange(newCompletions);
            await dbContext.SaveChangesAsync(ct);
        }

        var periodIds = periods.Select(p => p.Id).ToList();
        var completions = await dbContext.RoutinePeriodCompletions
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
                    return g.Take(depth)
                        .Reverse()
                        .Select(c => new PeriodCompletionRecord(c.PeriodStart, c.PeriodEnd, c.CompletedCount, c.TotalCount))
                        .ToList();
                }
            );

        var data = periods
            .Select(tp => new RoutineTodoListGroupedResponse
            {
                RoutineTimePeriod = routineTimePeriodMapper.ToResponse(tp) with
                {
                    NextResetAt = RoutineResetService.ComputeNextReset(tp),
                    CompletionHistory = completionsByPeriod.GetValueOrDefault(tp.Id, [])
                },
                Items = tp.RoutineTodoListColl
                    .OrderBy(e => e.IsDone).ThenBy(e => e.DisplayOrder)
                    .Select(mapper.ToResponse)
                    .ToList()
            })
            .ToList();

        await SendOkAsync(data, ct);
    }
}
