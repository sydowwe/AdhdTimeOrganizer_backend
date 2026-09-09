using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using Sydowwe.Framework.application.dto.response.generic;
using Sydowwe.Framework.application.endpoint.@base.read;

namespace AdhdTimeOrganizer.TodoLists.application.endpoint.todoList.taskPriority.query;

public class GetSelectOptionsTaskPriorityEndpoint(
    DbContext dbContext)
    : BaseGetSelectOptionsEndpoint<TaskPriority>(dbContext)
{
    protected override IQueryable<SelectOptionResponse> Map(IQueryable<TaskPriority> query)
    {
        return query.Select(e => new SelectOptionResponse
        {
            Id = e.Id,
            Text = e.Text
        });
    }
}