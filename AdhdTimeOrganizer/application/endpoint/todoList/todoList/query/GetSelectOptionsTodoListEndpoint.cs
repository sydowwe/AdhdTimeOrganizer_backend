using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;
using Sydowwe.Framework.application.dto.response.generic;
using Sydowwe.Framework.application.endpoint.@base.read;

namespace AdhdTimeOrganizer.application.endpoint.todoList.todoList.query;

public class GetSelectOptionsTodoListEndpoint(AppDbContext appDbContext) : BaseGetSelectOptionsEndpoint<TodoList>(appDbContext)
{
    protected override IQueryable<SelectOptionResponse> Map(IQueryable<TodoList> query)
    {
        return query.Select(t => new SelectOptionResponse(t.Id, t.Name));
    }
}