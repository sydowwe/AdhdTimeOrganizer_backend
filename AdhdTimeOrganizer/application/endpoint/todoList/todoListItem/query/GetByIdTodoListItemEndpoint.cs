using AdhdTimeOrganizer.application.dto.response.todoList;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.todoList.todoListItem.query;

public class GetByIdTodoListItemEndpoint(AppDbContext dbContext)
    : BaseGetByIdEndpoint<TodoListItem, TodoListItemResponse>(dbContext)
{
}
