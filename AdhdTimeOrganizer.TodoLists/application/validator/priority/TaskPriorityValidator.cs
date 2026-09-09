using AdhdTimeOrganizer.TodoLists.application.dto.request.todoList;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.TodoLists.application.validator;

public class TaskPriorityValidator : Validator<TaskPriorityRequest>
{
    public TaskPriorityValidator()
    {
        // The DTO is int, like the entity and the response. The ceiling is stated rather than
        // implied by the wire type: it used to be short.MaxValue only because the DTO was a short.
        RuleFor(x => x.Priority)
            .InclusiveBetween(1, short.MaxValue);
    }
}