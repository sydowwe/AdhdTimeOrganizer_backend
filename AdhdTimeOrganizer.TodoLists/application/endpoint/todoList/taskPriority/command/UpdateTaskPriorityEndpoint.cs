using AdhdTimeOrganizer.TodoLists.application.dto.request.todoList;
using AdhdTimeOrganizer.TodoLists.application.validator;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.TodoLists.application.endpoint.todoList.taskPriority.command;

public class UpdateTaskPriorityEndpoint(DbContext dbContext)
    : BaseUpdateEndpoint<TaskPriority, TaskPriorityRequest>(dbContext)
{
    public override void Configure()
    {
        base.Configure();
        Validator<TaskPriorityValidator>();
    }
}