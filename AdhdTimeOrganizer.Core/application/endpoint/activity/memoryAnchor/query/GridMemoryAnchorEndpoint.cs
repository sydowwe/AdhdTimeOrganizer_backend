using AdhdTimeOrganizer.Core.application.dto.filter;
using AdhdTimeOrganizer.Core.application.dto.response.activity.memoryAnchor;
using AdhdTimeOrganizer.Core.domain.model.entity.activity.memoryAnchor;
using Sydowwe.Framework.application.endpoint.@base.read.pageFilterSort;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.Core.application.endpoint.activity.memoryAnchor.query;

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