using AdhdTimeOrganizer.application.dto.request.filter;
using AdhdTimeOrganizer.application.dto.response.activity;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.application.mapper.activity;
using AdhdTimeOrganizer.domain.model.entity.activity;
using AdhdTimeOrganizer.infrastructure.persistence;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.activity;

public class GetActivitiesFilteredPaginatedEndpoint(
    AppCommandDbContext dbContext,
    ActivityMapper mapper) 
    : BaseFilteredPaginatedEndpoint<Activity, ActivityResponse, ActivityFilterRequest>(dbContext)
{
    private readonly ActivityMapper _mapper = mapper;

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

        if (filter.IsOnToDoList.HasValue)
        {
            query = query.Where(a => a.IsOnToDoList == filter.IsOnToDoList.Value);
        }

        if (filter.IsUnavoidable.HasValue)
        {
            query = query.Where(a => a.IsUnavoidable == filter.IsUnavoidable.Value);
        }

        if (filter.IsOnRoutineToDoList.HasValue)
        {
            query = query.Where(a => a.IsOnRoutineToDoList == filter.IsOnRoutineToDoList.Value);
        }

        if (filter.RoleId.HasValue)
        {
            query = query.Where(a => a.RoleId == filter.RoleId.Value);
        }

        if (filter.CategoryId.HasValue)
        {
            query = query.Where(a => a.CategoryId == filter.CategoryId.Value);
        }

        if (filter.UserId.HasValue)
        {
            query = query.Where(a => a.UserId == filter.UserId.Value);
        }

        return query;
    }

    protected override ActivityResponse MapToResponse(Activity entity)
    {
        return _mapper.ToResponse(entity);
    }
}
