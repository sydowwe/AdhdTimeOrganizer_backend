using AdhdTimeOrganizer.application.dto.response.generic;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.todoList.taskPriority.query;

public class GetSelectOptionsTaskPriorityEndpoint(
    AppDbContext appDbContext)
    : BaseGetSelectOptionsEndpoint<TaskPriority>(appDbContext)
{
    protected override IQueryable<SelectOptionResponse> Map(IQueryable<TaskPriority> query)
        => query.Select(e => new SelectOptionResponse
        {
            Id = e.Id,
            Text = e.Text
        });
}