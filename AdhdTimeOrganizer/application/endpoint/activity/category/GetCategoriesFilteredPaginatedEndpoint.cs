
using AdhdTimeOrganizer.application.dto.filter;
using AdhdTimeOrganizer.application.dto.request.filter;
using AdhdTimeOrganizer.application.dto.response;
using AdhdTimeOrganizer.application.dto.response.activity;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.application.mapper.activity;
using AdhdTimeOrganizer.domain.model.entity.activity;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activity;

public class GetCategoriesFilteredPaginatedEndpoint(
    AppCommandDbContext dbContext,
    CategoryMapper mapper) 
    : BaseFilteredPaginatedEndpoint<ActivityCategory, CategoryResponse, CategoryFilterRequest>(dbContext)
{
    private readonly CategoryMapper _mapper = mapper;

    protected override IQueryable<ActivityCategory> ApplyCustomFiltering(IQueryable<ActivityCategory> query, CategoryFilterRequest filter)
    {
        if (!string.IsNullOrWhiteSpace(filter.Name))
        {
            query = query.Where(c => c.Name.Contains(filter.Name));
        }

        if (!string.IsNullOrWhiteSpace(filter.Text))
        {
            query = query.Where(c => c.Text != null && c.Text.Contains(filter.Text));
        }

        if (!string.IsNullOrWhiteSpace(filter.Color))
        {
            query = query.Where(c => c.Color.Contains(filter.Color));
        }

        if (filter.UserId.HasValue)
        {
            query = query.Where(c => c.UserId == filter.UserId.Value);
        }

        return query;
    }

    protected override CategoryResponse MapToResponse(ActivityCategory entity)
    {
        return _mapper.ToResponse(entity);
    }
}
