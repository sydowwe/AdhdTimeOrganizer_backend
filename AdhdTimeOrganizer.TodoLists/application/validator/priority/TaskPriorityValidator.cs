using AdhdTimeOrganizer.TodoLists.application.dto.request.todoList;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.TodoLists.application.validator;

public class TaskPriorityValidator : Validator<TaskPriorityRequest>
{
    public TaskPriorityValidator()
    {
        RuleFor(x => x.Priority)
            .GreaterThanOrEqualTo((short)1);
    }
}