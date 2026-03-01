using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.todoList.todoListCategory.command;

public class DeleteTodoListCategoryEndpoint(AppDbContext dbContext)
    : BaseDeleteEndpoint<TodoListCategory>(dbContext);
