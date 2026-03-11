using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.application.mapper.activityPlanning;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;
using TodoListCategoryMapper = AdhdTimeOrganizer.application.mapper.todoList.TodoListCategoryMapper;

namespace AdhdTimeOrganizer.application.endpoint.todoList.todoListCategory.query;

public class GetSelectOptionsTodoListCategoryEndpoint(AppDbContext dbContext, TodoListCategoryMapper mapper)
    : BaseGetSelectOptionsEndpoint<TodoListCategory, TodoListCategoryMapper>(dbContext, mapper)
{
    protected override IQueryable<TodoListCategory> Sort(IQueryable<TodoListCategory> query)
        => query.OrderBy(c => c.Name);
}
