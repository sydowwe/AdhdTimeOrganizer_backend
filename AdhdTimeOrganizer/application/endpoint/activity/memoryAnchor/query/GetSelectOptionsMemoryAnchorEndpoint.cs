using AdhdTimeOrganizer.domain.model.entity.activity.memoryAnchor;
using AdhdTimeOrganizer.infrastructure.persistence;
using Sydowwe.Framework.application.dto.response.generic;
using Sydowwe.Framework.application.endpoint.@base.read;

namespace AdhdTimeOrganizer.application.endpoint.activity.memoryAnchor.query;

public class GetSelectOptionsMemoryAnchorEndpoint(AppDbContext dbContext)
    : BaseGetSelectOptionsEndpoint<MemoryAnchor>(dbContext)
{
    protected override IQueryable<SelectOptionResponse> Map(IQueryable<MemoryAnchor> query) => throw new NotImplementedException();
}