using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using Sydowwe.Framework.application.dto.response.generic;
using Sydowwe.Framework.application.endpoint.@base.read;

namespace AdhdTimeOrganizer.TodoLists.application.endpoint.todoList.todoList.query;

public class GetSelectOptionsTodoListEndpoint(DbContext appDbContext) : BaseGetSelectOptionsEndpoint<TodoList>(appDbContext)
{
    protected override IQueryable<SelectOptionResponse> Map(IQueryable<TodoList> query)
    {
        return query.Select(t => new SelectOptionResponse(t.Id, t.Name));
    }
}