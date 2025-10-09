using AdhdTimeOrganizer.application.dto.request.toDoList;
using AdhdTimeOrganizer.application.dto.response.activityPlanning;
using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.application.mapper.activityPlanning;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.TaskPriority.command;

public class TaskPriorityUpdateEndpoint(AppCommandDbContext dbContext, TaskPriorityMapper mapper)
    : BaseUpdateEndpoint<TaskPriority, TaskPriorityRequest, TaskPriorityResponse, TaskPriorityMapper>(dbContext, mapper);
