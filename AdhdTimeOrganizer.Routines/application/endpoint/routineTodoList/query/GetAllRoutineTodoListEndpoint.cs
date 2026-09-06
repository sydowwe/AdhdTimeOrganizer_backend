using AdhdTimeOrganizer.Routines.application.dto.response.todoList;
using AdhdTimeOrganizer.Routines.application.service.routine;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using Sydowwe.Framework.application.endpoint.@base.read;

namespace AdhdTimeOrganizer.Routines.application.endpoint.todoList.routineTodoList.query;

public class GetAllRoutineTodoListEndpoint(
    DbContext dbContext)
    : BaseGetAllEndpoint<RoutineTodoList, RoutineTodoListResponse>(dbContext)
{
    // Own field rather than the captured primary-ctor parameter: the read bases have no `protected DbContext`
    // the way BaseCreate/BaseUpdate do, and capturing a parameter that is also passed to the base is CS9107.
    private readonly DbContext _dbContext = dbContext;

    /// <summary>
    /// Swaps the nested period for a refreshed, fully derived one. The nested copy comes out of an expression
    /// tree, so on its own it carries an epoch <c>NextResetAt</c> and an empty heatmap — and it is a period the
    /// client renders freeze affordances from, so it owes the same lazy refresh every other period read does.
    /// See <see cref="RoutineTimePeriodReadRefresher"/>.
    /// </summary>
    protected override async Task<List<RoutineTodoListResponse>> PostProcessItemsAsync(
        List<RoutineTodoListResponse> items,
        CancellationToken ct)
    {
        var refreshed = await RoutineTimePeriodReadRefresher.RefreshAndRenderAsync(
            _dbContext, items.Select(i => i.RoutineTimePeriod.Id), DateTime.UtcNow, ct);

        return items
            .Select(i => refreshed.TryGetValue(i.RoutineTimePeriod.Id, out var period)
                ? i with { RoutineTimePeriod = period }
                : i)
            .ToList();
    }
}
