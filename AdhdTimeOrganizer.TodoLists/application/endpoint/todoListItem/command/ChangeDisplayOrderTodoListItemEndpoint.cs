using System.Linq.Expressions;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using AdhdTimeOrganizer.TodoLists.infrastructure.settings;
using Microsoft.Extensions.Options;

namespace AdhdTimeOrganizer.TodoLists.application.endpoint.todoList.todoListItem.command;

public class ChangeDisplayOrderTodoListItemEndpoint(DbContext dbContext, IOptions<TodoListSettings> settings) : BaseChangeDisplayOrderTodoListEndpoint<TodoListItem>(dbContext, settings)
{
    protected override Expression<Func<TodoListItem, long>> GroupFilterExpression => e => e.TaskPriorityId;
}