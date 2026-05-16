using AdhdTimeOrganizer.application.dto.filter;
using AdhdTimeOrganizer.application.dto.response;
using AdhdTimeOrganizer.application.dto.response.activity;
using AdhdTimeOrganizer.application.endpoint.@base.read.pageFilterSort;
using AdhdTimeOrganizer.application.mapper.activity;
using AdhdTimeOrganizer.domain.model.entity.activity;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activity.category.query;

public class GetFetchTableActivityCategoryEndpoint(
    AppDbContext dbContext,
    ActivityCategoryMapper mapper) 
    : BaseFetchTableEndpoint<ActivityCategory, ActivityCategoryResponse, CategoryFilterRequest, ActivityCategoryMapper>(dbContext, mapper)
{
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
            query = query.Where(c => c.Color == filter.Color);
        }

        return query;
    }
}
