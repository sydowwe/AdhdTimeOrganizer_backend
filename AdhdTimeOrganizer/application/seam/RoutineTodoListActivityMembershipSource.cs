using AdhdTimeOrganizer.Core.application.seam;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.config.dependencyInjection;

namespace AdhdTimeOrganizer.application.seam;

/// <summary>
/// Publishes "this activity is on a routine to-do list" through Core's seam.
/// </summary>
/// <remarks>
/// The sibling <c>TodoListActivityMembershipSource</c> already lives in
/// <c>AdhdTimeOrganizer.TodoLists</c>; this one sits host-side only because <see cref="RoutineTodoList"/>
/// has not been extracted yet. <b>Move it into <c>AdhdTimeOrganizer.Routines</c> along with the entity</b> -
/// nothing else needs to change when you do, since consumers resolve it by
/// <see cref="ActivityMembershipSourceKeys.RoutineTodoList"/> rather than by type.
/// <para>
/// The facet is <c>TimePeriodId</c>. As with the to-do source, no user scoping is applied here: the
/// query is composed into the caller's and <c>AppDbContext</c>'s global <c>IEntityWithUser</c> filter
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
