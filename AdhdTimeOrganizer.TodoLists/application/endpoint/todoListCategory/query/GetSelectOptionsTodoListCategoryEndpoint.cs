using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using Sydowwe.Framework.application.dto.response.generic;
using Sydowwe.Framework.application.endpoint.@base.read;

namespace AdhdTimeOrganizer.TodoLists.application.endpoint.todoList.todoListCategory.query;

public class GetSelectOptionsTodoListCategoryEndpoint(DbContext dbContext)
    : BaseGetSelectOptionsEndpoint<TodoListCategory>(dbContext)
{
    protected override IQueryable<TodoListCategory> Sort(IQueryable<TodoListCategory> query)
    {
        return query.OrderBy(c => c.Name);
    }

    protected override IQueryable<SelectOptionResponse> Map(IQueryable<TodoListCategory> query)
    {
        return query.Select(c => new SelectOptionResponse(c.Id, c.Name));
    }
}