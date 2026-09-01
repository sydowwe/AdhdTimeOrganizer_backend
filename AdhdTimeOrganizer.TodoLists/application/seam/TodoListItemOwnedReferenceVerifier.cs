using AdhdTimeOrganizer.Core.application.seam;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.config.dependencyInjection;

namespace AdhdTimeOrganizer.TodoLists.application.seam;

/// <summary>
/// Answers "is this <see cref="TodoListItem"/> id the caller's?" through Core's seam, so a slice
/// writing <c>TodoListItemId</c> onto its own row (History's recordings, today) can refuse a stranger's
/// id without referencing TodoLists.
/// </summary>
/// <remarks>
/// Filters on the supplied <c>userId</c> rather than leaning on the ambient <c>IEntityWithUser</c>
/// query filter — unlike <see cref="TodoListActivityMembershipSource"/>, which composes into a caller's
/// query and can. This one answers a security question, and the answer must not change with whoever
/// happens to be executing.
/// </remarks>
public sealed class TodoListItemOwnedReferenceVerifier : IUserOwnedReferenceVerifier, IScopedService
{
    public string Key => UserOwnedReferenceKeys.TodoListItem;

    public Task<bool> ExistsForUserAsync(DbContext db, long userId, long id, CancellationToken ct) =>
        db.Set<TodoListItem>().AnyAsync(i => i.Id == id && i.UserId == userId, ct);
}
