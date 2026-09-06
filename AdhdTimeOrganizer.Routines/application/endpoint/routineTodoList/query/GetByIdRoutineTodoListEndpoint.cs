using AdhdTimeOrganizer.Routines.application.dto.response.todoList;
using AdhdTimeOrganizer.Routines.application.service.routine;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using Sydowwe.Framework.application.endpoint.@base.read;

namespace AdhdTimeOrganizer.Routines.application.endpoint.todoList.routineTodoList.query;

public class GetByIdRoutineTodoListEndpoint(
    DbContext dbContext)
    : BaseGetByIdEndpoint<RoutineTodoList, RoutineTodoListResponse>(dbContext)
{
    // Own field rather than the captured primary-ctor parameter: the read bases have no `protected DbContext`
    // the way BaseCreate/BaseUpdate do, and capturing a parameter that is also passed to the base is CS9107.
    private readonly DbContext _dbContext = dbContext;

    // Scoped by AppDbContext's global IEntityWithUser query filter, so a foreign row never projects.
    protected override Task<bool> AuthorizeAsync(RoutineTodoListResponse entity, CancellationToken ct) => Task.FromResult(true);

    /// <summary>
    /// Same reason as <see cref="GetAllRoutineTodoListEndpoint"/> — see
    /// <see cref="RoutineTimePeriodReadRefresher"/>.
    /// </summary>
    protected override async Task<RoutineTodoListResponse> PostProcessAsync(
        RoutineTodoListResponse entity,
        CancellationToken ct)
    {
        var refreshed = await RoutineTimePeriodReadRefresher.RefreshAndRenderAsync(
            _dbContext, [entity.RoutineTimePeriod.Id], DateTime.UtcNow, ct);

        return refreshed.TryGetValue(entity.RoutineTimePeriod.Id, out var period)
            ? entity with { RoutineTimePeriod = period }
            : entity;
    }
}
