using AdhdTimeOrganizer.Core.application.dto.response.activity;
using AdhdTimeOrganizer.Core.domain.model.@enum;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.endpoint.@base.read;

namespace AdhdTimeOrganizer.Core.application.endpoint.activity.role.query;

/// <summary>
/// <c>GET /activity-role/by-system-key/{key}</c> — the identity-based replacement for
/// <see cref="GetByNameActivityRoleEndpoint"/> on the three roles the app itself references. The
/// client sends the camelCase <see cref="SystemActivityRole"/> value verbatim as the path segment.
///
/// <para>A pure read: a missing role is a 404, never a lazy create. Deleting a keyed role is refused
/// (<c>DeleteActivityRoleEndpoint</c>) and renaming one keeps the key, so for any account seeded
/// after this shipped the 404 is unreachable — it survives only for accounts that renamed a role
/// before the key existed and so could not be backfilled by name.</para>
///
/// <para>The row is scoped to the caller by <c>AppDbContext</c>'s global <c>IEntityWithUser</c> query
/// filter, same as every other role read.</para>
/// </summary>
public class GetBySystemKeyActivityRoleEndpoint(DbContext dbContext)
    : BaseGetByFieldEndpoint<ActivityRole, ActivityRoleResponse>(dbContext)
{
    /// <summary>
    /// Kebab-case on purpose: the base interpolates this into the route verbatim
    /// (<c>by-{FieldName}</c>) and only kebaberizes the entity name, so <c>nameof(SystemKey)</c> would
    /// publish <c>by-SystemKey</c> — which a request for <c>by-system-key</c> does not match, hyphens
    /// not being a case difference.
    /// </summary>
    protected override string FieldName => "system-key";

    protected override IQueryable<ActivityRole> FilterByField(IQueryable<ActivityRole> query, string value)
    {
        // An unrecognised key is "no such role" (404), not a 500 — and it must not fall through to
        // `SystemKey == null`, which would match every user-created role.
        return Enum.TryParse<SystemActivityRole>(value, ignoreCase: true, out var key)
            ? query.Where(r => r.SystemKey == key)
            : query.Take(0);
    }
}
