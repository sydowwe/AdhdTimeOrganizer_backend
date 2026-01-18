using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.todoList.routineTodoList.command;

public class RoutineToggleIsDoneTodoListEndpoint(AppCommandDbContext dbContext) : BaseToggleIsDoneTodoListEndpoint<RoutineTodoList>(dbContext);
