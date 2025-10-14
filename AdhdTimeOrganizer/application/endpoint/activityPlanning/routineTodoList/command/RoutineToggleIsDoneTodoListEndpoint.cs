using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning;

public class RoutineToggleIsDoneTodoListEndpoint(AppCommandDbContext dbContext) : BaseToggleIsDoneTodoListEndpoint<RoutineTodoList>(dbContext);
