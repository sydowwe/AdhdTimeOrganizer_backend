using AdhdTimeOrganizer.Core.application.dto.response.activity.memoryAnchor;
using AdhdTimeOrganizer.Core.domain.model.entity.activity.memoryAnchor;
using Sydowwe.Framework.application.endpoint.@base.read;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.Core.application.endpoint.activity.memoryAnchor.query;

public class GetByIdMemoryAnchorEndpoint(DbContext dbContext)
    : BaseGetByIdEndpoint<MemoryAnchor, MemoryAnchorResponse>(dbContext)
{
    // Scoped by AppDbContext's global IEntityWithUser query filter, so a foreign row never projects.
    protected override Task<bool> AuthorizeAsync(MemoryAnchorResponse entity, CancellationToken ct) => Task.FromResult(true);
}