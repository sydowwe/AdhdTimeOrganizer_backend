using AdhdTimeOrganizer.application.endpoint.todoList;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.plannerTask.command;

public class ToggleIsDonePlannerTaskEndpoint(AppDbContext dbContext) : BaseToggleIsDoneEndpoint<PlannerTask>(dbContext);