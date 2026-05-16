using AdhdTimeOrganizer.application.dto.request.todoList;
using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.application.mapper.activityPlanning;
using AdhdTimeOrganizer.application.validator;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;
using TodoListCategoryMapper = AdhdTimeOrganizer.application.mapper.todoList.TodoListCategoryMapper;

namespace AdhdTimeOrganizer.application.endpoint.todoList.todoListCategory.command;

public class CreateTodoListCategoryEndpoint(AppDbContext dbContext, TodoListCategoryMapper mapper)
    : BaseCreateEndpoint<TodoListCategory, TodoListCategoryRequest, TodoListCategoryMapper>(dbContext, mapper)
{
    public override void Configure()
    {
        base.Configure();
        Validator<TodoListCategoryValidator>();
    }
}
