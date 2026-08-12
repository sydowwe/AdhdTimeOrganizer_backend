using System.Linq.Expressions;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using AdhdTimeOrganizer.TodoLists.application.endpoint.todoList;
using AdhdTimeOrganizer.TodoLists.infrastructure.settings;
using Microsoft.Extensions.Options;

namespace AdhdTimeOrganizer.Routines.application.endpoint.todoList.routineTodoList.command;

public class ChangeDisplayOrderRoutineTodoListEndpoint(DbContext dbContext, IOptions<TodoListSettings> settings) : BaseChangeDisplayOrderTodoListEndpoint<RoutineTodoList>(dbContext, settings)
{
    protected override Expression<Func<RoutineTodoList, long>> GroupFilterExpression => e => e.TimePeriodId;
}