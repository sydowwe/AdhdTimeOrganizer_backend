using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.Routines.application.endpoint.todoList.routineTodoList.command;

public class DeleteRoutineTodoListEndpoint(DbContext dbContext)
    : BaseDeleteEndpoint<RoutineTodoList>(dbContext);