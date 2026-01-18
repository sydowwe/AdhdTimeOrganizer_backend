using AdhdTimeOrganizer.application.dto.request.toDoList;
using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.application.mapper.activityPlanning;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.todoList.taskPriority.command;

public class UpdateTaskPriorityEndpoint(AppCommandDbContext dbContext, TaskPriorityMapper mapper)
    : BaseUpdateEndpoint<TaskPriority, TaskPriorityRequest, TaskPriorityMapper>(dbContext, mapper);
