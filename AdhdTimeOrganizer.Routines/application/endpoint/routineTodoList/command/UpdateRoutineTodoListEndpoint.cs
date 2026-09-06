using AdhdTimeOrganizer.Core.application.endpoint.extensions;
using AdhdTimeOrganizer.Core.domain.serviceContract;
using AdhdTimeOrganizer.Routines.application.dto.request.todoList;
using AdhdTimeOrganizer.Routines.application.validator;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using AdhdTimeOrganizer.Routines.infrastructure.persistence.extensions;
using AdhdTimeOrganizer.TodoLists.application.dto.request.todoList;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using AdhdTimeOrganizer.TodoLists.infrastructure.settings;
using Microsoft.Extensions.Options;
using Sydowwe.Framework.application.endpoint.@base.command;
using Sydowwe.Framework.application.extensions;

namespace AdhdTimeOrganizer.Routines.application.endpoint.todoList.routineTodoList.command;

public class UpdateRoutineTodoListEndpoint(
    DbContext dbContext,
    IOptions<TodoListSettings> settings,
    IReferencedIdOwnershipService ownership)
    : BaseUpdateEndpoint<RoutineTodoList, UpdateRoutineTodoListRequest>(dbContext)
{
    private readonly TodoListSettings _settings = settings.Value;

    public override void Configure()
    {
        base.Configure();
        Validator<UpdateRoutineTodoListValidator>();
    }

    /// <summary>
    /// The edit rewrites <c>ActivityId</c>, so it can repoint one of the caller's own routines at
    /// another user's activity. Same guard as the create path.
    /// </summary>
    protected override Task<bool> BeforeMapping(UpdateRoutineTodoListRequest req, CancellationToken ct = default) =>
        this.EnsureOwnsActivityAsync(req, ownership, DbContext, ct);

    protected override async Task<bool> AfterMapping(RoutineTodoList entity, UpdateRoutineTodoListRequest req, CancellationToken ct = default)
    {
        if (req.Steps is not null)
            entity.Steps = ReconcileSteps(entity.Steps, req.Steps);

        // TotalCount is client-owned, DoneCount is not (see UpdateRoutineTodoListRequest), so the count is
        // re-derived rather than taken from the body: an item that becomes counted starts at zero, and
        // lowering the total must not leave a count above it — CK_RoutineTodoList_DoneCount_LessOrEqual_TotalCount
        // would refuse the save.
        if (entity.TotalCount is { } totalCount)
            entity.DoneCount = Math.Min(entity.DoneCount ?? 0, totalCount);

        // DisplayOrder is scoped per period — the create path derives it per period and
        // ChangeDisplayOrderRoutineTodoListEndpoint groups on TimePeriodId — so an item that moves would
        // otherwise carry an order that means nothing in its destination and can tie with one already there.
        var originalTimePeriodId = DbContext.Entry(entity).Property(e => e.TimePeriodId).OriginalValue;
        if (originalTimePeriodId != entity.TimePeriodId)
            entity.DisplayOrder = await DbContext.Set<RoutineTodoList>()
                .GetNextDisplayOrder(_settings, User.GetId(), entity.TimePeriodId, ct);

        return true;
    }

    /// <summary>
    /// Reconciles the steps in place: a request step carrying an id keeps that id and its <c>IsDone</c> while
    /// its name, order and note are overwritten, one without an id is new, and one the request omits is gone.
    /// </summary>
    /// <remarks>
    /// Rebuilding the collection from fresh <see cref="TodoListStep"/> instances instead would mint a new
    /// <see cref="Guid"/> per step on every save — <c>Id</c> defaults to <c>Guid.NewGuid()</c> and the column
    /// is <c>ValueGeneratedNever()</c> — and three step routes take that id in the path, so an unrelated note
    /// edit would 404 every step id the client is holding.
    /// </remarks>
    private static List<TodoListStep> ReconcileSteps(ICollection<TodoListStep> existing, List<StepRequest> requested)
    {
        return requested.Select(s =>
        {
            var step = s.Id.HasValue ? existing.FirstOrDefault(e => e.Id == s.Id.Value) : null;
            if (step is null)
                return new TodoListStep { Name = s.Name, Order = s.Order, Note = s.Note };

            step.Name = s.Name;
            step.Order = s.Order;
            step.Note = s.Note;
            return step;
        }).ToList();
    }
}
