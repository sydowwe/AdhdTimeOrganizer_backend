using AdhdTimeOrganizer.Core.application.dto.filter;
using AdhdTimeOrganizer.Core.application.dto.response.activity;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.endpoint.@base.read.pageFilterSort;

namespace AdhdTimeOrganizer.Core.application.endpoint.activity.category.query;

public class GridActivityCategoryEndpoint(
    DbContext dbContext)
    : BaseGridEndpoint<ActivityCategory, ActivityCategoryResponse, CategoryFilterRequest>(dbContext)
{
    protected override IQueryable<ActivityCategory> ApplyCustomFiltering(IQueryable<ActivityCategory> query, CategoryFilterRequest filter)
    {
        if (!string.IsNullOrWhiteSpace(filter.Name))
            query = query.Where(c => c.Name.Contains(filter.Name));

        if (!string.IsNullOrWhiteSpace(filter.Text))
            query = query.Where(c => c.Text != null && c.Text.Contains(filter.Text));

        if (!string.IsNullOrWhiteSpace(filter.Color))
            query = query.Where(c => c.Color == filter.Color);

        return query;
    }
}