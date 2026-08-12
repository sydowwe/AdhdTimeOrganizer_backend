using AdhdTimeOrganizer.Core.application.dto.filter;
using AdhdTimeOrganizer.Core.application.dto.response.activity;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.endpoint.@base.read.pageFilterSort;

namespace AdhdTimeOrganizer.Core.application.endpoint.activity.activity.query;

public class GridActivityEndpoint(
    DbContext dbContext)
    : BaseGridEndpoint<Activity, ActivityResponse, ActivityFilterRequest>(dbContext)
{
    protected override IQueryable<Activity> ApplyCustomFiltering(IQueryable<Activity> query, ActivityFilterRequest filter)
    {
        if (!string.IsNullOrWhiteSpace(filter.Name))
            query = query.Where(a => a.Name.Contains(filter.Name));

        if (!string.IsNullOrWhiteSpace(filter.Text))
            query = query.Where(a => a.Text != null && a.Text.Contains(filter.Text));

        if (filter.IsUnavoidable.HasValue)
            query = query.Where(a => a.IsUnavoidable == filter.IsUnavoidable.Value);

        if (!string.IsNullOrWhiteSpace(filter.RoleName))
            query = query.Where(a => a.Role.Name.Contains(filter.RoleName));

        if (!string.IsNullOrWhiteSpace(filter.CategoryName))
            query = query.Where(a => a.Category != null && a.Category.Name.Contains(filter.CategoryName));

        if (filter.RoleId.HasValue)
            query = query.Where(a => a.RoleId == filter.RoleId.Value);

        if (filter.CategoryId.HasValue)
            query = query.Where(a => a.CategoryId == filter.CategoryId.Value);

        return query;
    }
}