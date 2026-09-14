using AdhdTimeOrganizer.ActivityProfiles.application.dto.response;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using AdhdTimeOrganizer.Core.infrastructure.persistence;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.endpoint.@base.read;

namespace AdhdTimeOrganizer.ActivityProfiles.application.endpoint.memoryAnchor.query;

public class GetByIdMemoryAnchorEndpoint(DbContext dbContext)
    : BaseGetByIdEndpoint<MemoryAnchor, MemoryAnchorResponse>(dbContext)
{
    // Scoped by AppDbContext's global IEntityWithUser query filter, so a foreign row never projects.
    protected override Task<bool> AuthorizeAsync(MemoryAnchorResponse entity, CancellationToken ct) => Task.FromResult(true);

    /// <summary>
    /// This is one of the get-by-ids that exists to open a row a filter hides. The anchor itself is never
    /// archived-filtered, but the response projects its Activity, which is — so an anchor of an archived
    /// activity answered 404 and the edit form could not be opened for the very rows anchoring is for.
    /// Lifted by name so the caller's own scoping is untouched; the grid lifts the same one, for the same
    /// projection.
    /// </summary>
    protected override string[] IgnoredQueryFilters => [AppQueryFilters.ActivityArchived];
}