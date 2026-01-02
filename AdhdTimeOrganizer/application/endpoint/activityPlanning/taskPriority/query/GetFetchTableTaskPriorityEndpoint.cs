using AdhdTimeOrganizer.application.dto.filter;
using AdhdTimeOrganizer.application.dto.response.activityPlanning;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.application.mapper.activityPlanning;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.taskPriority.query;

public class GetFetchTableTaskPriorityEndpoint(
    AppCommandDbContext dbContext,
    TaskPriorityMapper mapper) 
    : BaseFetchTableEndpoint<TaskPriority, TaskPriorityResponse, TaskPriorityFilterRequest, TaskPriorityMapper>(dbContext, mapper)
{
    private readonly TaskPriorityMapper _mapper = mapper;

    protected override IQueryable<TaskPriority> ApplyCustomFiltering(IQueryable<TaskPriority> query, TaskPriorityFilterRequest filter)
    {
        if (!string.IsNullOrWhiteSpace(filter.Text))
        {
            query = query.Where(tu => tu.Text != null && tu.Text.Contains(filter.Text));
        }

        if (!string.IsNullOrWhiteSpace(filter.Color))
        {
            query = query.Where(tu => tu.Color.Contains(filter.Color));
        }

        if (filter.MinPriority.HasValue)
        {
            query = query.Where(tu => tu.Priority >= filter.MinPriority.Value);
        }

        if (filter.MaxPriority.HasValue)
        {
            query = query.Where(tu => tu.Priority <= filter.MaxPriority.Value);
        }

        if (filter.UserId.HasValue)
        {
            query = query.Where(tu => tu.UserId == filter.UserId.Value);
        }

        return query;
    }
}
