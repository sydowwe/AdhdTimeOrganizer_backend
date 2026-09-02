using AdhdTimeOrganizer.Core.application.seam;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using Sydowwe.Framework.config.dependencyInjection;

namespace AdhdTimeOrganizer.TodoLists.application.seam;

/// <summary>
/// Publishes "this activity is on a to-do list" through Core's seam, so a slice that wants to filter on
/// it does not have to reference TodoLists. History's grid was that slice until <c>CQ-1</c> deleted it;
/// there is no consumer at present — see <see cref="IActivityMembershipSource"/>.
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
