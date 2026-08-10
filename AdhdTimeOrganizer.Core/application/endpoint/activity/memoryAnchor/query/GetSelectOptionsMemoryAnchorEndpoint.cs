using AdhdTimeOrganizer.Core.domain.model.entity.activity.memoryAnchor;
using Sydowwe.Framework.application.dto.response.generic;
using Sydowwe.Framework.application.endpoint.@base.read;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.Core.application.endpoint.activity.memoryAnchor.query;

public class GetSelectOptionsMemoryAnchorEndpoint(DbContext dbContext)
    : BaseGetSelectOptionsEndpoint<MemoryAnchor>(dbContext)
{
    protected override IQueryable<SelectOptionResponse> Map(IQueryable<MemoryAnchor> query) => throw new NotImplementedException();
}