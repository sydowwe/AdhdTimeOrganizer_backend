using AdhdTimeOrganizer.Routines.application.dto.response.todoList;
using AdhdTimeOrganizer.Routines.application.service.routine;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using Sydowwe.Framework.application.endpoint.@base.read;

namespace AdhdTimeOrganizer.Routines.application.endpoint.todoList.routineTimePeriod.query;

public class GetByIdRoutineTimePeriodEndpoint(
    DbContext dbContext)
    : BaseGetByIdEndpoint<RoutineTimePeriod, RoutineTimePeriodResponse>(dbContext)
{
    // Own field rather than the captured primary-ctor parameter: the read bases have no `protected DbContext`
    // the way BaseCreate/BaseUpdate do, and capturing a parameter that is also passed to the base is CS9107.
    private readonly DbContext _dbContext = dbContext;

    // Scoped by AppDbContext's global IEntityWithUser query filter, so a foreign row never projects.
    protected override Task<bool> AuthorizeAsync(RoutineTimePeriodResponse entity, CancellationToken ct) => Task.FromResult(true);

    /// <summary>
    /// Same reason as <see cref="GetAllRoutineTimePeriodEndpoint"/>: the projection alone serves an epoch
    /// <c>NextResetAt</c>, an empty heatmap, an unrefilled freeze budget and a streak whose grace window may
    /// have lapsed. See <see cref="RoutineTimePeriodReadRefresher"/>.
    /// </summary>
    protected override async Task<RoutineTimePeriodResponse> PostProcessAsync(
        RoutineTimePeriodResponse entity,
        CancellationToken ct)
    {
        var refreshed = await RoutineTimePeriodReadRefresher.RefreshAndRenderAsync(
            _dbContext, [entity.Id], DateTime.UtcNow, ct);

        return refreshed.GetValueOrDefault(entity.Id, entity);
    }
}
