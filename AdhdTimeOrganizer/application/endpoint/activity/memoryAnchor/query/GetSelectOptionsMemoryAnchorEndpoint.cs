using AdhdTimeOrganizer.application.dto.response.generic;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.domain.model.entity.activity.memoryAnchor;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activity.memoryAnchor.query;

public class GetSelectOptionsMemoryAnchorEndpoint(AppDbContext dbContext)
    : BaseGetSelectOptionsEndpoint<MemoryAnchor>(dbContext)
{
    protected override IQueryable<SelectOptionResponse> Map(IQueryable<MemoryAnchor> query)
    {
        throw new NotImplementedException();
    }
}
