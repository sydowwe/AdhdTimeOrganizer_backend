using AdhdTimeOrganizer.application.dto.filter;
using AdhdTimeOrganizer.application.dto.response.activity.memoryAnchor;
using AdhdTimeOrganizer.application.endpoint.@base.read.pageFilterSort;
using AdhdTimeOrganizer.application.mapper;
using AdhdTimeOrganizer.domain.model.entity.activity.memoryAnchor;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activity.memoryAnchor.query;

public class GridMemoryAnchorEndpoint(AppDbContext dbContext, MemoryAnchorMapper mapper)
    : BaseGridEndpoint<MemoryAnchor, MemoryAnchorResponse, MemoryAnchorFilterRequest, MemoryAnchorMapper>(dbContext, mapper)
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
