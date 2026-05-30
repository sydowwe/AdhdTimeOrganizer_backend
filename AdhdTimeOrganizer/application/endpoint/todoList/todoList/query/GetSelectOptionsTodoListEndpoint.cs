using AdhdTimeOrganizer.application.dto.response.generic;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.todoList.todoList.query;

public class GetSelectOptionsTodoListEndpoint(AppDbContext appDbContext) : BaseGetSelectOptionsEndpoint<TodoList>(appDbContext)
{
    protected override IQueryable<SelectOptionResponse> Map(IQueryable<TodoList> query) =>
        query.Select(t => new SelectOptionResponse(t.Id, t.Name));
}