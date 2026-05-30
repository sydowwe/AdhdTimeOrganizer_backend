using AdhdTimeOrganizer.application.dto.request.todoList;
using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.application.validator;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.todoList.todoListCategory.command;

public class CreateTodoListCategoryEndpoint(AppDbContext dbContext)
    : BaseCreateEndpoint<TodoListCategory, TodoListCategoryRequest>(dbContext)
{
    public override void Configure()
    {
        base.Configure();
        Validator<TodoListCategoryValidator>();
    }
}
