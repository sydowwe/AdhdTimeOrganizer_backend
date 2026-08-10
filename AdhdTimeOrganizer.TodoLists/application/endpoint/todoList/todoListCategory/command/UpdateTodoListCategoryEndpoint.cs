using AdhdTimeOrganizer.TodoLists.application.dto.request.todoList;
using AdhdTimeOrganizer.TodoLists.application.validator;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.TodoLists.application.endpoint.todoList.todoListCategory.command;

public class UpdateTodoListCategoryEndpoint(DbContext dbContext)
    : BaseUpdateEndpoint<TodoListCategory, TodoListCategoryRequest>(dbContext)
{
    public override void Configure()
    {
        base.Configure();
        Validator<TodoListCategoryValidator>();
    }
}