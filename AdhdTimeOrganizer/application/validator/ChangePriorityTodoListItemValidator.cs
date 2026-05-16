using AdhdTimeOrganizer.application.dto.request.todoList;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.application.validator;

public class ChangePriorityTodoListItemValidator : Validator<ChangePriorityTodoListItemRequest>
{
    public ChangePriorityTodoListItemValidator()
    {
        RuleFor(x => x.PriorityId).GreaterThan(0);
    }
}
