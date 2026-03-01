using System.Linq.Expressions;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;
using AdhdTimeOrganizer.infrastructure.settings;
using Microsoft.Extensions.Options;

namespace AdhdTimeOrganizer.application.endpoint.todoList.todoListItem.command;

public class ChangeDisplayOrderTodoListItemEndpoint(AppDbContext dbContext, IOptions<TodoListSettings> settings) : BaseChangeDisplayOrderTodoListEndpoint<TodoListItem>(dbContext, settings)
{
    protected override Expression<Func<TodoListItem, long>> GroupFilterExpression => e => e.TaskPriorityId;
}
