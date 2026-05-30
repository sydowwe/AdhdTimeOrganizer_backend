using AdhdTimeOrganizer.application.dto.response.todoList;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.todoList.todoListCategory.query;

public class GetByIdTodoListCategoryEndpoint(AppDbContext dbContext)
    : BaseGetByIdEndpoint<TodoListCategory, TodoListCategoryResponse>(dbContext);
