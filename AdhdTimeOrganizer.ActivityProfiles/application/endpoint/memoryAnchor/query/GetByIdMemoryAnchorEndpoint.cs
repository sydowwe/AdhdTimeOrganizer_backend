using AdhdTimeOrganizer.ActivityProfiles.application.dto.response;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.endpoint.@base.read;

namespace AdhdTimeOrganizer.ActivityProfiles.application.endpoint.memoryAnchor.query;

public class GetByIdMemoryAnchorEndpoint(DbContext dbContext)
    : BaseGetByIdEndpoint<MemoryAnchor, MemoryAnchorResponse>(dbContext)
{
    // Scoped by AppDbContext's global IEntityWithUser query filter, so a foreign row never projects.
    protected override Task<bool> AuthorizeAsync(MemoryAnchorResponse entity, CancellationToken ct) => Task.FromResult(true);
}