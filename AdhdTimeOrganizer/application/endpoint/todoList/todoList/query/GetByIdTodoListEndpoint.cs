using AdhdTimeOrganizer.application.dto.response.toDoList;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.application.mapper.activityPlanning;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;
using TodoListMapper = AdhdTimeOrganizer.application.mapper.todoList.TodoListMapper;

namespace AdhdTimeOrganizer.application.endpoint.todoList.todoList.query;

public class GetByIdTodoListEndpoint(AppDbContext dbContext, TodoListMapper mapper)
    : BaseGetByIdEndpoint<TodoList, TodoListResponse, TodoListMapper>(dbContext, mapper);
