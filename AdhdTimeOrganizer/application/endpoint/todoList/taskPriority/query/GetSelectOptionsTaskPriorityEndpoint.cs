using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.application.mapper.activityPlanning;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.todoList.taskPriority.query;

public class GetSelectOptionsTaskPriorityEndpoint(
    AppCommandDbContext appDbContext,
    TaskPriorityMapper mapper)
    : BaseGetSelectOptionsEndpoint<TaskPriority, TaskPriorityMapper>(appDbContext, mapper)
{
}
