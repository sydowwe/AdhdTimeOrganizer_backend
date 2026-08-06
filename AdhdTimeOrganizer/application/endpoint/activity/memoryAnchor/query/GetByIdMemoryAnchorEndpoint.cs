using AdhdTimeOrganizer.application.dto.response.activity.memoryAnchor;
using AdhdTimeOrganizer.domain.model.entity.activity.memoryAnchor;
using AdhdTimeOrganizer.infrastructure.persistence;
using Sydowwe.Framework.application.endpoint.@base.read;

namespace AdhdTimeOrganizer.application.endpoint.activity.memoryAnchor.query;

public class GetByIdMemoryAnchorEndpoint(AppDbContext dbContext)
    : BaseGetByIdEndpoint<MemoryAnchor, MemoryAnchorResponse>(dbContext)
{
    // Scoped by AppDbContext's global IEntityWithUser query filter, so a foreign row never projects.
    protected override Task<bool> AuthorizeAsync(MemoryAnchorResponse entity, CancellationToken ct) => Task.FromResult(true);
}