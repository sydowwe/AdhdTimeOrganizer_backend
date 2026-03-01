using AdhdTimeOrganizer.application.dto.request.toDoList;
using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.application.mapper.activityPlanning;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.todoList.todoList.command;

public class UpdateTodoListEndpoint(AppDbContext dbContext, TodoListMapper mapper)
    : BaseUpdateEndpoint<TodoList, TodoListRequest, TodoListMapper>(dbContext, mapper);
