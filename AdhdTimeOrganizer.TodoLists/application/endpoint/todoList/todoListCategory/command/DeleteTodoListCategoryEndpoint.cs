using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.TodoLists.application.endpoint.todoList.todoListCategory.command;

public class DeleteTodoListCategoryEndpoint(DbContext dbContext)
    : BaseDeleteEndpoint<TodoListCategory>(dbContext);