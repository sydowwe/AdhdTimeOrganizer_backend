using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.application.endpoint.todoList.routineTodoList.command;

public class DeleteRoutineTodoListEndpoint(AppDbContext dbContext)
    : BaseDeleteEndpoint<RoutineTodoList>(dbContext);