using AdhdTimeOrganizer.TodoLists.application.dto.request.todoList;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.TodoLists.application.validator;

public class ChangePriorityTodoListItemValidator : Validator<ChangePriorityTodoListItemRequest>
{
    public ChangePriorityTodoListItemValidator()
    {
        RuleFor(x => x.PriorityId).GreaterThan(0);
    }
}