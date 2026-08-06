using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;
using Sydowwe.Framework.application.dto.response.generic;
using Sydowwe.Framework.application.endpoint.@base.read;

namespace AdhdTimeOrganizer.application.endpoint.todoList.taskPriority.query;

public class GetSelectOptionsTaskPriorityEndpoint(
    AppDbContext appDbContext)
    : BaseGetSelectOptionsEndpoint<TaskPriority>(appDbContext)
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