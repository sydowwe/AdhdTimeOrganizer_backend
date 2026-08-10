using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.TodoLists.application.endpoint.todoList.todoList.command;

public class DeleteTodoListEndpoint(DbContext dbContext)
    : BaseDeleteEndpoint<TodoList>(dbContext);