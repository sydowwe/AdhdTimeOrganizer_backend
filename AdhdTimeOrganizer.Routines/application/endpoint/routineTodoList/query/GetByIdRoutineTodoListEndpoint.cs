using AdhdTimeOrganizer.Core.application.seam;
using AdhdTimeOrganizer.Routines.application.dto.response.todoList;
using AdhdTimeOrganizer.Routines.application.service.routine;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using Sydowwe.Framework.application.endpoint.@base.read;
using Sydowwe.Framework.application.extensions;

namespace AdhdTimeOrganizer.Routines.application.endpoint.todoList.routineTodoList.query;

public class GetByIdRoutineTodoListEndpoint(
    DbContext dbContext,
    IRoutineItemLoggedTimeSource loggedTime)
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
        var now = DateTime.UtcNow;
        var refreshed = await RoutineTimePeriodReadRefresher.RefreshAndRenderAsync(
            _dbContext, [entity.RoutineTimePeriod.Id], now, ct);

        var withPeriod = refreshed.TryGetValue(entity.RoutineTimePeriod.Id, out var period)
            ? entity with { RoutineTimePeriod = period }
            : entity;

        // F2-06. The list re-reads one item here after time is logged against it, so the figure must survive that.
        var filled = await RoutineItemTypicalDurationFiller.FillAsync([withPeriod], _dbContext, loggedTime, User.GetId(), now, ct);
        return filled[0];
    }
}
