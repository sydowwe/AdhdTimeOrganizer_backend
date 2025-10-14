using System.Linq.Expressions;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;
using AdhdTimeOrganizer.infrastructure.settings;
using Microsoft.Extensions.Options;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.todoList.command;

public class ChangeDisplayOrderTodoListEndpoint(AppCommandDbContext dbContext, IOptions<TodoListSettings> settings) : BaseChangeDisplayOrderTodoListEndpoint<TodoList>(dbContext, settings)
{
    //TODO add after more todo lists are implemented
    protected override Expression<Func<TodoList, long>> GroupFilterExpression => e => e.TaskPriorityId;
}