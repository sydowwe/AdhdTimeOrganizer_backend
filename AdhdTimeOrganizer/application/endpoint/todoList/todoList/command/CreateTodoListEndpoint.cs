using AdhdTimeOrganizer.application.dto.request.toDoList;
using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.application.mapper.activityPlanning;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;
using TodoListMapper = AdhdTimeOrganizer.application.mapper.todoList.TodoListMapper;

namespace AdhdTimeOrganizer.application.endpoint.todoList.todoList.command;

public class CreateTodoListEndpoint(AppDbContext dbContext, TodoListMapper mapper)
    : BaseCreateEndpoint<TodoList, TodoListRequest, TodoListMapper>(dbContext, mapper);
