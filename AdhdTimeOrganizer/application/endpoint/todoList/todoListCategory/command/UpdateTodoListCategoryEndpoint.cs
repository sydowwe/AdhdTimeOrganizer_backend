using AdhdTimeOrganizer.application.dto.request.toDoList;
using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.application.mapper.activityPlanning;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;
using TodoListCategoryMapper = AdhdTimeOrganizer.application.mapper.todoList.TodoListCategoryMapper;

namespace AdhdTimeOrganizer.application.endpoint.todoList.todoListCategory.command;

public class UpdateTodoListCategoryEndpoint(AppDbContext dbContext, TodoListCategoryMapper mapper)
    : BaseUpdateEndpoint<TodoListCategory, TodoListCategoryRequest, TodoListCategoryMapper>(dbContext, mapper);
