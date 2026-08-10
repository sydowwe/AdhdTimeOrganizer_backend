using AdhdTimeOrganizer.Core.application.seam;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.config.dependencyInjection;

namespace AdhdTimeOrganizer.TodoLists.application.seam;

/// <summary>
/// Publishes "this activity is on a to-do list" through Core's seam, so slices that want to filter on
/// it (History's grid, today) do not have to reference TodoLists.
/// </summary>
/// <remarks>
/// The facet is <c>TaskPriorityId</c>. No user scoping here on purpose: the query is composed into the
/// caller's, and <c>AppDbContext</c>'s global <c>IEntityWithUser</c> filter applies to
/// <see cref="TodoListItem"/> like everything else, so the subquery is already scoped to the ambient
/// user by the time it reaches Postgres.
/// </remarks>
public sealed class TodoListActivityMembershipSource : IActivityMembershipSource, IScopedService
{
    public string Key => ActivityMembershipSourceKeys.TodoList;

    public IQueryable<long> ActivityIds(DbContext db, long? facetId) =>
        db.Set<TodoListItem>()
            .Where(i => facetId == null || i.TaskPriorityId == facetId)
            .Select(i => i.ActivityId);
}
