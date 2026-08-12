using AdhdTimeOrganizer.ActivityProfiles.application.dto.filter;
using AdhdTimeOrganizer.ActivityProfiles.application.dto.response;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.endpoint.@base.read.pageFilterSort;

namespace AdhdTimeOrganizer.ActivityProfiles.application.endpoint.memoryAnchor.query;

public class GridMemoryAnchorEndpoint(DbContext dbContext)
    : BaseGridEndpoint<MemoryAnchor, MemoryAnchorResponse, MemoryAnchorFilterRequest>(dbContext)
{
    protected override IQueryable<MemoryAnchor> ApplyCustomFiltering(IQueryable<MemoryAnchor> query, MemoryAnchorFilterRequest filter)
    {
        if (filter.AnchorMonth.HasValue)
            query = query.Where(m => m.AnchorMonth == filter.AnchorMonth.Value);

        if (filter.AnchorYear.HasValue)
            query = query.Where(m => m.AnchorYear == filter.AnchorYear.Value);

        if (filter.ActivityId.HasValue)
            query = query.Where(m => m.ActivityId == filter.ActivityId.Value);

        return query;
    }
}