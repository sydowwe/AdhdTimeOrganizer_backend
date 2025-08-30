using AdhdTimeOrganizer.application.dto.request.filter;
using AdhdTimeOrganizer.application.dto.response.activityPlanning;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.application.mapper.activityPlanning;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning;

public class GetPlannerTasksFilteredPaginatedEndpoint(
    AppCommandDbContext dbContext,
    PlannerTaskMapper mapper) 
    : BaseFilteredPaginatedEndpoint<PlannerTask, PlannerTaskResponse, PlannerTaskFilterRequest>(dbContext)
{
    private readonly PlannerTaskMapper _mapper = mapper;

    protected override IQueryable<PlannerTask> WithIncludes(IQueryable<PlannerTask> query)
    {
        return query
            .Include(pt => pt.Activity)
                .ThenInclude(a => a.Role)
            .Include(pt => pt.Activity)
                .ThenInclude(a => a.Category);
    }

    protected override IQueryable<PlannerTask> ApplyCustomFiltering(IQueryable<PlannerTask> query, PlannerTaskFilterRequest filter)
    {
        query = query.Where(task => task.StartTimestamp >= filter.FromTimeStamp &&
                            task.StartTimestamp.AddMinutes(task.MinuteLength) <= filter.ToTimeStamp);

        if (filter.ActivityId.HasValue)
        {
            query = query.Where(pt => pt.ActivityId == filter.ActivityId.Value);
        }

        if (filter.RoleId.HasValue)
            query = query.Where(h => h.Activity.CategoryId == filter.RoleId);

        if (filter.CategoryId.HasValue)
            query = query.Where(h => h.Activity.RoleId == filter.CategoryId);

        if (filter.MinMinuteLength.HasValue)
        {
            query = query.Where(pt => pt.MinuteLength >= filter.MinMinuteLength.Value);
        }

        if (filter.MaxMinuteLength.HasValue)
        {
            query = query.Where(pt => pt.MinuteLength <= filter.MaxMinuteLength.Value);
        }

        if (filter.IsDone.HasValue)
        {
            query = query.Where(pt => pt.IsDone == filter.IsDone.Value);
        }

        return query;
    }

    protected override PlannerTaskResponse MapToResponse(PlannerTask entity)
    {
        return _mapper.ToResponse(entity);
    }
}
