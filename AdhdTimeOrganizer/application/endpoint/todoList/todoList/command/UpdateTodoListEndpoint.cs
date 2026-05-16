using AdhdTimeOrganizer.application.dto.request.todoList;
using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.application.mapper.activityPlanning;
using AdhdTimeOrganizer.application.validator;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;
using TodoListMapper = AdhdTimeOrganizer.application.mapper.todoList.TodoListMapper;

namespace AdhdTimeOrganizer.application.endpoint.todoList.todoList.command;

public class UpdateTodoListEndpoint(AppDbContext dbContext, TodoListMapper mapper)
    : BaseUpdateEndpoint<TodoList, TodoListRequest, TodoListMapper>(dbContext, mapper)
{
    public override void Configure()
    {
        base.Configure();
        Validator<TodoListValidator>();
    }
}
