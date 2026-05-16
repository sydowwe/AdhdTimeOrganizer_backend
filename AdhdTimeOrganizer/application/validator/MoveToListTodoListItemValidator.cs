using AdhdTimeOrganizer.application.dto.request.todoList;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.application.validator;

public class MoveToListTodoListItemValidator : Validator<MoveToListTodoListItemRequest>
{
    public MoveToListTodoListItemValidator()
    {
        RuleFor(x => x.DestinationListId).GreaterThan(0);
    }
}
