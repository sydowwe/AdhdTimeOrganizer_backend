using AdhdTimeOrganizer.application.dto.request.toDoList;
using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.application.mapper.activityPlanning;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.todoList.todoListCategory.command;

public class CreateTodoListCategoryEndpoint(AppDbContext dbContext, TodoListCategoryMapper mapper)
    : BaseCreateEndpoint<TodoListCategory, TodoListCategoryRequest, TodoListCategoryMapper>(dbContext, mapper);
