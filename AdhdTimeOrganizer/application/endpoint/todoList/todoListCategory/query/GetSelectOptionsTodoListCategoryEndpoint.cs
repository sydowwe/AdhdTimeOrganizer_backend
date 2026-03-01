using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.application.mapper.activityPlanning;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.todoList.todoListCategory.query;

public class GetSelectOptionsTodoListCategoryEndpoint(AppDbContext dbContext, TodoListCategoryMapper mapper)
    : BaseGetSelectOptionsEndpoint<TodoListCategory, TodoListCategoryMapper>(dbContext, mapper)
{
    protected override IQueryable<TodoListCategory> Sort(IQueryable<TodoListCategory> query)
        => query.OrderBy(c => c.Name);
}
