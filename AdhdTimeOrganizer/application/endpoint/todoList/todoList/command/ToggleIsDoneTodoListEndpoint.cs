using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning;

public class ToggleIsDoneTodoListEndpoint(AppCommandDbContext dbContext) : BaseToggleIsDoneTodoListEndpoint<TodoList>(dbContext);