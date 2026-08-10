using AdhdTimeOrganizer.TodoLists.application.dto.request.todoList;
using FastEndpoints;
using FluentValidation;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;

namespace AdhdTimeOrganizer.TodoLists.application.validator;

public class TaskPriorityValidator : Validator<TaskPriorityRequest>
{
    public TaskPriorityValidator()
    {
        RuleFor(x => x.Priority)
            .GreaterThanOrEqualTo((short)1);
    }
}