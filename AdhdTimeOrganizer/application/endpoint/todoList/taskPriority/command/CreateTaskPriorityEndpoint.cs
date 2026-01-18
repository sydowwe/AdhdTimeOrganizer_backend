using AdhdTimeOrganizer.application.dto.request.toDoList;
using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.application.mapper.activityPlanning;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.todoList.taskPriority.command;

public class CreateTaskPriorityEndpoint(AppCommandDbContext dbContext, TaskPriorityMapper mapper)
    : BaseCreateEndpoint<TaskPriority, TaskPriorityRequest, TaskPriorityMapper>(dbContext, mapper);
