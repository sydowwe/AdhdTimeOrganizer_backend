using AdhdTimeOrganizer.application.dto.request.todoList;
using AdhdTimeOrganizer.application.validator;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.application.endpoint.todoList.todoListCategory.command;

public class UpdateTodoListCategoryEndpoint(AppDbContext dbContext)
    : BaseUpdateEndpoint<TodoListCategory, TodoListCategoryRequest>(dbContext)
{
    public override void Configure()
    {
        base.Configure();
        Validator<TodoListCategoryValidator>();
    }
}