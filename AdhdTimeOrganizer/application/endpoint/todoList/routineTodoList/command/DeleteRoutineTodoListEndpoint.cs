using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.todoList.routineTodoList.command;

public class DeleteRoutineTodoListEndpoint(AppCommandDbContext dbContext)
    : BaseDeleteEndpoint<RoutineTodoList>(dbContext);
