using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.dto.response.generic;
using Sydowwe.Framework.application.endpoint.@base.read;

namespace AdhdTimeOrganizer.ActivityProfiles.application.endpoint.memoryAnchor.query;

public class GetSelectOptionsMemoryAnchorEndpoint(DbContext dbContext)
    : BaseGetSelectOptionsEndpoint<MemoryAnchor>(dbContext)
{
    protected override IQueryable<SelectOptionResponse> Map(IQueryable<MemoryAnchor> query)
    {
        return query.Select(e => new SelectOptionResponse
        {
            Id = e.Id,
            Text = e.HighlightNote
        });
    }
}