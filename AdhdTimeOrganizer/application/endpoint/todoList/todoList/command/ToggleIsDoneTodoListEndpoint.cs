using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.todoList.todoList.command;

public class ToggleIsDoneTodoListEndpoint(AppDbContext dbContext) : BaseToggleIsDoneTodoListEndpoint<TodoList>(dbContext);