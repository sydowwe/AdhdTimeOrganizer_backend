using System.Linq.Expressions;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;
using AdhdTimeOrganizer.infrastructure.settings;
using Microsoft.Extensions.Options;

namespace AdhdTimeOrganizer.application.endpoint.todoList.routineTodoList.command;

public class ChangeDisplayOrderRoutineTodoListEndpoint(AppCommandDbContext dbContext, IOptions<TodoListSettings> settings) : BaseChangeDisplayOrderTodoListEndpoint<RoutineTodoList>(dbContext, settings)
{
    protected override Expression<Func<RoutineTodoList, long>> GroupFilterExpression => e => e.TimePeriodId;
}