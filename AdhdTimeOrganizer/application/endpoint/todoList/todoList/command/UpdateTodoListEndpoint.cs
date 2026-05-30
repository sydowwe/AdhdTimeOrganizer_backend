using AdhdTimeOrganizer.application.dto.request.todoList;
using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.application.validator;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.todoList.todoList.command;

public class UpdateTodoListEndpoint(AppDbContext dbContext)
    : BaseUpdateEndpoint<TodoList, TodoListRequest>(dbContext)
{
    public override void Configure()
    {
        base.Configure();
        Validator<TodoListValidator>();
    }
}
