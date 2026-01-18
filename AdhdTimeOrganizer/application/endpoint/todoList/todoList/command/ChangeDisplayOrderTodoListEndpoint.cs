using System.Linq.Expressions;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;
using AdhdTimeOrganizer.infrastructure.settings;
using Microsoft.Extensions.Options;

namespace AdhdTimeOrganizer.application.endpoint.todoList.todoList.command;

public class ChangeDisplayOrderTodoListEndpoint(AppCommandDbContext dbContext, IOptions<TodoListSettings> settings) : BaseChangeDisplayOrderTodoListEndpoint<TodoList>(dbContext, settings)
{
    //TODO add after more todo lists are implemented
    protected override Expression<Func<TodoList, long>> GroupFilterExpression => e => e.TaskPriorityId;
}