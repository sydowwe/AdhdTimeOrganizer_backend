using AdhdTimeOrganizer.TodoLists.application.dto.request.todoList;
using AdhdTimeOrganizer.TodoLists.application.validator;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.TodoLists.application.endpoint.todoList.todoList.command;

public class UpdateTodoListEndpoint(DbContext dbContext)
    : BaseUpdateEndpoint<TodoList, TodoListRequest>(dbContext)
{
    public override void Configure()
    {
        base.Configure();
        Validator<TodoListValidator>();
    }
}