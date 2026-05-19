using AdhdTimeOrganizer.application.dto.filter;
using AdhdTimeOrganizer.application.dto.response;
using AdhdTimeOrganizer.application.dto.response.activity;
using AdhdTimeOrganizer.application.endpoint.@base.read.pageFilterSort;
using AdhdTimeOrganizer.application.mapper.activity;
using AdhdTimeOrganizer.domain.model.entity.activity;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activity.role.query;

public class GridActivityRoleEndpoint(
    AppDbContext dbContext,
    ActivityRoleMapper mapper) 
    : BaseGridEndpoint<ActivityRole, ActivityRoleResponse, RoleFilterRequest, ActivityRoleMapper>(dbContext, mapper)
{
    protected override IQueryable<ActivityRole> ApplyCustomFiltering(IQueryable<ActivityRole> query, RoleFilterRequest filter)
    {
        if (!string.IsNullOrWhiteSpace(filter.Name))
        {
            query = query.Where(r => r.Name.Contains(filter.Name));
        }

        if (!string.IsNullOrWhiteSpace(filter.Text))
        {
            query = query.Where(r => r.Text != null && r.Text.Contains(filter.Text));
        }

        if (!string.IsNullOrWhiteSpace(filter.Color))
        {
            query = query.Where(r => r.Color == filter.Color);
        }

        if (!string.IsNullOrWhiteSpace(filter.Icon))
        {
            query = query.Where(r => r.Icon != null && r.Icon.Contains(filter.Icon));
        }

        return query;
    }
}
