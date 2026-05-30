using AdhdTimeOrganizer.application.dto.filter;
using AdhdTimeOrganizer.application.dto.response.taskPlanner;
using AdhdTimeOrganizer.application.endpoint.@base.read.pageFilterSort;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.todoList.taskPriority.query;

public class GridTaskPriorityEndpoint(
    AppDbContext dbContext) 
    : BaseGridEndpoint<TaskPriority, TaskPriorityResponse, TaskPriorityFilterRequest>(dbContext)
{
    protected override IQueryable<TaskPriority> ApplyCustomFiltering(IQueryable<TaskPriority> query, TaskPriorityFilterRequest filter)
    {
        if (!string.IsNullOrWhiteSpace(filter.Text))
        {
            query = query.Where(tu => tu.Text.Contains(filter.Text));
        }

        if (!string.IsNullOrWhiteSpace(filter.Color))
        {
            query = query.Where(tu => tu.Color == filter.Color);
        }

        if (filter.MinPriority.HasValue)
        {
            query = query.Where(tu => tu.Priority >= filter.MinPriority.Value);
        }

        if (filter.MaxPriority.HasValue)
        {
            query = query.Where(tu => tu.Priority <= filter.MaxPriority.Value);
        }

        return query;
    }
}
