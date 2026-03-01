using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.todoList.todoListItem.command;

public class ToggleIsDoneTodoListItemEndpoint(AppDbContext dbContext) : BaseToggleIsDoneTodoListEndpoint<TodoListItem>(dbContext);
