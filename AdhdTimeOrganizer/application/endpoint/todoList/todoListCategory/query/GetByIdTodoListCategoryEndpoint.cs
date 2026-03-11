using AdhdTimeOrganizer.application.dto.response.toDoList;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.application.mapper.activityPlanning;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;
using TodoListCategoryMapper = AdhdTimeOrganizer.application.mapper.todoList.TodoListCategoryMapper;

namespace AdhdTimeOrganizer.application.endpoint.todoList.todoListCategory.query;

public class GetByIdTodoListCategoryEndpoint(AppDbContext dbContext, TodoListCategoryMapper mapper)
    : BaseGetByIdEndpoint<TodoListCategory, TodoListCategoryResponse, TodoListCategoryMapper>(dbContext, mapper);
