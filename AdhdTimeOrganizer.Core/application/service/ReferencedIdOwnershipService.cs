using AdhdTimeOrganizer.Core.application.seam;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.Core.domain.serviceContract;
using AdhdTimeOrganizer.infrastructure.persistence;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.config.dependencyInjection;

namespace AdhdTimeOrganizer.Core.application.service;

/// <summary>
/// Checks client-supplied ids against the caller: <c>Activity</c> directly, everything another slice
/// owns through the <see cref="IUserOwnedReferenceVerifier"/> seam.
/// </summary>
/// <remarks>
/// Both methods answer <c>false</c> for "does not exist" and for "belongs to someone else" without
/// distinguishing them, so callers can only ever turn the result into a 404. Telling the two apart
/// would make the endpoint an existence oracle over another account's ids.
/// </remarks>
public sealed class ReferencedIdOwnershipService(IEnumerable<IUserOwnedReferenceVerifier> verifiers)
    : IReferencedIdOwnershipService, IScopedService
{
    private readonly IReadOnlyList<IUserOwnedReferenceVerifier> _verifiers = verifiers.ToList();

    /// <inheritdoc />
    public Task<bool> OwnsActivityAsync(DbContext db, long userId, long activityId, CancellationToken ct) =>
        db.Set<Activity>()
            // Lifts archival and nothing else — an archived activity is still the caller's, and a
            // recording already pointing at one must stay editable. The per-user filter stays on; the
            // explicit UserId comparison is what actually decides, so this does not depend on it.
            .IgnoreQueryFilters([AppQueryFilters.ActivityArchived])
            .AnyAsync(a => a.Id == activityId && a.UserId == userId, ct);

    /// <inheritdoc />
    public Task<bool> OwnsActivityRoleAsync(DbContext db, long userId, long roleId, CancellationToken ct) =>
        db.Set<ActivityRole>().AnyAsync(r => r.Id == roleId && r.UserId == userId, ct);

    /// <inheritdoc />
    public Task<bool> OwnsActivityCategoryAsync(DbContext db, long userId, long categoryId, CancellationToken ct) =>
        db.Set<ActivityCategory>().AnyAsync(c => c.Id == categoryId && c.UserId == userId, ct);

    /// <inheritdoc />
    public async Task<bool> OwnsReferenceAsync(DbContext db, string key, long userId, long id, CancellationToken ct)
    {
        var verifier = _verifiers.FirstOrDefault(v => v.Key == key);

        // Fail closed. The opposite default — "no verifier, so nothing to check" — is the shape that
        // let these ids through unvalidated in the first place, and it would come back the moment a
        // slice fell out of ModuleAssemblies. A rejected write is visible; an unchecked one is not.
        if (verifier == null)
            return false;

        return await verifier.ExistsForUserAsync(db, userId, id, ct);
    }
}
