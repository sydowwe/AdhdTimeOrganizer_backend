using AdhdTimeOrganizer.application.dto.request.todoList;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.application.validator;

public class TaskPriorityValidator : Validator<TaskPriorityRequest>
{
    public TaskPriorityValidator()
    {
        RuleFor(x => x.Priority)
            .GreaterThanOrEqualTo((short)1);
    }
}
