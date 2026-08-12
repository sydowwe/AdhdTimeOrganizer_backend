using AdhdTimeOrganizer.Core.application.seam;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using Sydowwe.Framework.config.dependencyInjection;

namespace AdhdTimeOrganizer.Routines.application.seam;

/// <summary>
/// Publishes "this activity is on a routine to-do list" through Core's seam.
/// </summary>
/// <remarks>
/// The sibling <c>TodoListActivityMembershipSource</c> lives in <c>AdhdTimeOrganizer.TodoLists</c>;
/// consumers resolve either one by <see cref="ActivityMembershipSourceKeys.RoutineTodoList"/> rather
/// than by type, so neither slice references the other.
/// <para>
/// The facet is <c>TimePeriodId</c>. As with the to-do source, no user scoping is applied here: the
/// query is composed into the caller's, and <c>AppDbContext</c>'s global <c>IEntityWithUser</c> filter
/// covers <see cref="RoutineTodoList"/>.
/// </para>
/// </remarks>
public sealed class RoutineTodoListActivityMembershipSource : IActivityMembershipSource, IScopedService
{
    public string Key => ActivityMembershipSourceKeys.RoutineTodoList;

    public IQueryable<long> ActivityIds(DbContext db, long? facetId) =>
        db.Set<RoutineTodoList>()
            .Where(r => facetId == null || r.TimePeriodId == facetId)
            .Select(r => r.ActivityId);
}
