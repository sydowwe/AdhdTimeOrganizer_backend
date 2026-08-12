using AdhdTimeOrganizer.TodoLists.application.dto.response.todoList;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using Sydowwe.Framework.application.endpoint.@base.read;

namespace AdhdTimeOrganizer.TodoLists.application.endpoint.todoList.taskPriority.query;

public class GetAllTaskPriorityEndpoint(
    DbContext dbContext)
    : BaseGetAllEndpoint<TaskPriority, TaskPriorityResponse>(dbContext)
{
}