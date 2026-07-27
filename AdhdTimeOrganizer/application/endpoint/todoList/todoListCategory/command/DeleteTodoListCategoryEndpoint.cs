using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.application.endpoint.todoList.todoListCategory.command;

public class DeleteTodoListCategoryEndpoint(AppDbContext dbContext)
    : BaseDeleteEndpoint<TodoListCategory>(dbContext);