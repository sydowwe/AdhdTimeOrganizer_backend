using AdhdTimeOrganizer.application.dto.request.todoList;
using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.application.validator;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.todoList.taskPriority.command;

public class CreateTaskPriorityEndpoint(AppDbContext dbContext)
    : BaseCreateEndpoint<TaskPriority, TaskPriorityRequest>(dbContext)
{
    public override void Configure()
    {
        base.Configure();
        Validator<TaskPriorityValidator>();
    }
}
