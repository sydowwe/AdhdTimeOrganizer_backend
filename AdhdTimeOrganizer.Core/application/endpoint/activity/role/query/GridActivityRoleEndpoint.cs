using AdhdTimeOrganizer.Core.application.dto.filter;
using AdhdTimeOrganizer.Core.application.dto.response.activity;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.endpoint.@base.read.pageFilterSort;

namespace AdhdTimeOrganizer.Core.application.endpoint.activity.role.query;

public class GridActivityRoleEndpoint(
    DbContext dbContext)
    : BaseGridEndpoint<ActivityRole, ActivityRoleResponse, RoleFilterRequest>(dbContext)
{
    protected override IQueryable<ActivityRole> ApplyCustomFiltering(IQueryable<ActivityRole> query, RoleFilterRequest filter)
    {
        if (!string.IsNullOrWhiteSpace(filter.Name))
            query = query.Where(r => r.Name.Contains(filter.Name));

        if (!string.IsNullOrWhiteSpace(filter.Text))
            query = query.Where(r => r.Text != null && r.Text.Contains(filter.Text));

        if (!string.IsNullOrWhiteSpace(filter.Color))
            query = query.Where(r => r.Color == filter.Color);

        if (!string.IsNullOrWhiteSpace(filter.Icon))
            query = query.Where(r => r.Icon != null && r.Icon.Contains(filter.Icon));

        return query;
    }
}