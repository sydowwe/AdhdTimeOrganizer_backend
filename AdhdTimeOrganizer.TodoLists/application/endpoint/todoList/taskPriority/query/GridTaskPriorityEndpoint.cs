using AdhdTimeOrganizer.TodoLists.application.dto.filter;
using AdhdTimeOrganizer.TodoLists.application.dto.response.todoList;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using Sydowwe.Framework.application.endpoint.@base.read.pageFilterSort;

namespace AdhdTimeOrganizer.TodoLists.application.endpoint.todoList.taskPriority.query;

public class GridTaskPriorityEndpoint(
    DbContext dbContext)
    : BaseGridEndpoint<TaskPriority, TaskPriorityResponse, TaskPriorityFilterRequest>(dbContext)
{
    protected override IQueryable<TaskPriority> ApplyCustomFiltering(IQueryable<TaskPriority> query, TaskPriorityFilterRequest filter)
    {
        if (!string.IsNullOrWhiteSpace(filter.Text))
            query = query.Where(tu => tu.Text.Contains(filter.Text));

        if (!string.IsNullOrWhiteSpace(filter.Color))
            query = query.Where(tu => tu.Color == filter.Color);

        if (filter.MinPriority.HasValue)
            query = query.Where(tu => tu.Priority >= filter.MinPriority.Value);

        if (filter.MaxPriority.HasValue)
            query = query.Where(tu => tu.Priority <= filter.MaxPriority.Value);

        return query;
    }
}