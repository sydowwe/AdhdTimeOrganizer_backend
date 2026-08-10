using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.TodoLists.infrastructure.persistence.extensions;
using AdhdTimeOrganizer.TodoLists.infrastructure.settings;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.infrastructure.persistence.extensions;

/// <summary>
/// The routine-shaped overload of <see cref="TodoListExtensions.GetNextDisplayOrder{TEntity}"/>.
/// <para>
/// It used to sit next to the generic method in <c>TodoListExtensions</c>, but it names
/// <see cref="RoutineTodoList"/> — a Routines entity — which would have made the TodoLists slice
/// depend on Routines and inverted the one-way edge the split is built on. The generic method stays
/// in TodoLists; only this grouping-by-time-period overload lives here, and it travels to
/// <c>AdhdTimeOrganizer.Routines</c> when that slice is extracted.
/// </para>
/// </summary>
public static class RoutineTodoListExtensions
{
    public static async Task<long> GetNextDisplayOrder(this DbSet<RoutineTodoList> dbSet, TodoListSettings settings, long userId, long timePeriodId, CancellationToken ct = default)
    {
        return await dbSet.GetNextDisplayOrder(settings, userId, e => e.TimePeriodId == timePeriodId, ct);
    }
}
