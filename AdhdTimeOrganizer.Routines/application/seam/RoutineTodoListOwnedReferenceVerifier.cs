using AdhdTimeOrganizer.Core.application.seam;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.config.dependencyInjection;

namespace AdhdTimeOrganizer.Routines.application.seam;

/// <summary>
/// Answers "is this <see cref="RoutineTodoList"/> id the caller's?" through Core's seam — the routine
/// half of <see cref="IUserOwnedReferenceVerifier"/>, consumed by History's write path.
/// </summary>
/// <remarks>
/// Filters on the supplied <c>userId</c> explicitly, not on the ambient query filter, for the reason
/// on the interface: this is a security answer, not a scoped read.
/// </remarks>
public sealed class RoutineTodoListOwnedReferenceVerifier : IUserOwnedReferenceVerifier, IScopedService
{
    public string Key => UserOwnedReferenceKeys.RoutineTodoList;

    public Task<bool> ExistsForUserAsync(DbContext db, long userId, long id, CancellationToken ct) =>
        db.Set<RoutineTodoList>().AnyAsync(r => r.Id == id && r.UserId == userId, ct);
}
