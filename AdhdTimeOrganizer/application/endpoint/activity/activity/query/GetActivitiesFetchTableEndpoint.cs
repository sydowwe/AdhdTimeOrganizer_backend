using AdhdTimeOrganizer.application.dto.filter;
using AdhdTimeOrganizer.application.dto.response.activity;
using AdhdTimeOrganizer.application.endpoint.@base.read.pageFilterSort;
using AdhdTimeOrganizer.application.mapper.activity;
using AdhdTimeOrganizer.domain.model.entity.activity;
using AdhdTimeOrganizer.infrastructure.persistence;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.activity.activity.query;

public class GetActivitiesFetchTableEndpoint(
    AppDbContext dbContext,
    ActivityMapper mapper) 
    : BaseFetchTableEndpoint<Activity, ActivityResponse, ActivityFilterRequest, ActivityMapper>(dbContext, mapper)
{
    protected override IQueryable<Activity> WithIncludes(IQueryable<Activity> query)
    {
        return query
            .Include(a => a.Role)
            .Include(a => a.Category);
    }

    protected override IQueryable<Activity> ApplyCustomFiltering(IQueryable<Activity> query, ActivityFilterRequest filter)
    {
        if (!string.IsNullOrWhiteSpace(filter.Name))
        {
            query = query.Where(a => a.Name.Contains(filter.Name));
        }

        if (!string.IsNullOrWhiteSpace(filter.Text))
        {
            query = query.Where(a => a.Text != null && a.Text.Contains(filter.Text));
        }

        if (filter.IsUnavoidable.HasValue)
        {
            query = query.Where(a => a.IsUnavoidable == filter.IsUnavoidable.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.RoleName))
        {
            query = query.Where(a => a.Role.Name.Contains(filter.RoleName));
        }

        if (!string.IsNullOrWhiteSpace(filter.CategoryName))
        {
            query = query.Where(a => a.Category != null && a.Category.Name.Contains(filter.CategoryName));
        }

        if (filter.RoleId.HasValue)
        {
            query = query.Where(a => a.RoleId == filter.RoleId.Value);
        }

        if (filter.CategoryId.HasValue)
        {
            query = query.Where(a => a.CategoryId == filter.CategoryId.Value);
        }

        return query;
    }
}
