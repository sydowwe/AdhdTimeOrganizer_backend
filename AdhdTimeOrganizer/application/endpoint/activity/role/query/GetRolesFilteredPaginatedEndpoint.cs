using AdhdTimeOrganizer.application.dto.request.filter;
using AdhdTimeOrganizer.application.dto.response;
using AdhdTimeOrganizer.application.dto.response.activity;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.application.mapper.activity;
using AdhdTimeOrganizer.domain.model.entity.activity;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activity;

public class GetRolesFilteredPaginatedEndpoint(
    AppCommandDbContext dbContext,
    ActivityRoleMapper mapper) 
    : BaseFilteredPaginatedEndpoint<ActivityRole, RoleResponse, RoleFilterRequest>(dbContext)
{
    private readonly ActivityRoleMapper _mapper = mapper;

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
            query = query.Where(r => r.Color.Contains(filter.Color));
        }

        if (!string.IsNullOrWhiteSpace(filter.Icon))
        {
            query = query.Where(r => r.Icon != null && r.Icon.Contains(filter.Icon));
        }

        if (filter.UserId.HasValue)
        {
            query = query.Where(r => r.UserId == filter.UserId.Value);
        }

        return query;
    }

    protected override RoleResponse MapToResponse(ActivityRole entity)
    {
        return _mapper.ToResponse(entity);
    }
}
