using AdhdTimeOrganizer.application.dto.response.generic;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.todoList.todoListCategory.query;

public class GetSelectOptionsTodoListCategoryEndpoint(AppDbContext dbContext)
    : BaseGetSelectOptionsEndpoint<TodoListCategory>(dbContext)
{
    protected override IQueryable<TodoListCategory> Sort(IQueryable<TodoListCategory> query)
        => query.OrderBy(c => c.Name);

    protected override IQueryable<SelectOptionResponse> Map(IQueryable<TodoListCategory> query) =>
        query.Select(c => new SelectOptionResponse(c.Id, c.Name));
}