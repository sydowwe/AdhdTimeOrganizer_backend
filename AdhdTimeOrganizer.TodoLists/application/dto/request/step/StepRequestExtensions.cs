using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;

namespace AdhdTimeOrganizer.TodoLists.application.dto.request.todoList;

public static class StepRequestExtensions
{
    /// <summary>
    /// Reconciles an item's steps in place against the request: a step carrying an id keeps that id and its
    /// <c>IsDone</c> while its name, order and note are overwritten, one without an id is new, and one the
    /// request omits is gone.
    /// </summary>
    /// <remarks>
    /// Rebuilding the collection from fresh <see cref="TodoListStep"/> instances instead would mint a new
    /// <see cref="Guid"/> per step on every save — <c>Id</c> defaults to <c>Guid.NewGuid()</c> and the column
    /// is <c>ValueGeneratedNever()</c> — and three step routes take that id in the path, so an unrelated note
    /// edit would 404 every step id the client is holding.
    /// <para>
    /// Shared by both PUT surfaces (<c>UpdateTodoListItemEndpoint</c> and <c>UpdateRoutineTodoListEndpoint</c>);
    /// they had the same collection to rebuild and only one of them was rebuilding it correctly.
    /// </para>
    /// </remarks>
    public static void ReconcileStepsFrom(this BaseTodoListItem entity, List<StepRequest> requested)
    {
        var existing = entity.Steps;
        entity.Steps = requested.Select(s =>
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
