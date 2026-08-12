using AdhdTimeOrganizer.TodoLists.application.dto.request.todoList;
using AdhdTimeOrganizer.TodoLists.application.validator;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.TodoLists.application.endpoint.todoList.todoListCategory.command;

public class CreateTodoListCategoryEndpoint(DbContext dbContext)
    : BaseCreateEndpoint<TodoListCategory, TodoListCategoryRequest>(dbContext)
{
    public override void Configure()
    {
        base.Configure();
        Validator<TodoListCategoryValidator>();
    }
}