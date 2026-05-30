using AdhdTimeOrganizer.application.dto.response.todoList;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.todoList.todoList.query;

public class GetByIdTodoListEndpoint(AppDbContext dbContext)
    : BaseGetByIdEndpoint<TodoList, TodoListResponse>(dbContext);
