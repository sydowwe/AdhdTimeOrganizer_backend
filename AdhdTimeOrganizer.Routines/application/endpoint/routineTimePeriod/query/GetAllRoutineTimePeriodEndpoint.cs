using AdhdTimeOrganizer.Routines.application.dto.response.todoList;
using AdhdTimeOrganizer.Routines.application.service.routine;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using Sydowwe.Framework.application.endpoint.@base.read;

namespace AdhdTimeOrganizer.Routines.application.endpoint.todoList.routineTimePeriod.query;

public class GetAllRoutineTimePeriodEndpoint(
    DbContext dbContext)
    : BaseGetAllEndpoint<RoutineTimePeriod, RoutineTimePeriodResponse>(dbContext)
{
    // Own field rather than the captured primary-ctor parameter: the read bases have no `protected DbContext`
    // the way BaseCreate/BaseUpdate do, and capturing a parameter that is also passed to the base is CS9107.
    private readonly DbContext _dbContext = dbContext;

    protected override IQueryable<RoutineTimePeriod> Sort(IQueryable<RoutineTimePeriod> query)
    {
        return query.OrderBy(x => x.LengthInDays);
    }

    /// <summary>
    /// The base serves <c>Projection</c> straight, which cannot compute <c>NextResetAt</c> or the heatmap and
    /// cannot run the two lazy refreshes — see <see cref="RoutineTimePeriodReadRefresher"/>. Re-deriving here is
    /// a second read of rows the projection already touched; that is the price of keeping the base's route,
    /// roles and sort rather than hand-rolling a fourth period read.
    /// </summary>
    protected override async Task<List<RoutineTimePeriodResponse>> PostProcessItemsAsync(
        List<RoutineTimePeriodResponse> items,
        CancellationToken ct)
    {
        var refreshed = await RoutineTimePeriodReadRefresher.RefreshAndRenderAsync(
            _dbContext, items.Select(i => i.Id), DateTime.UtcNow, ct);

        // Map over `items`, not over the dictionary: Sort's order is the contract, and a dictionary has none.
        return items.Select(i => refreshed.GetValueOrDefault(i.Id, i)).ToList();
    }
}
