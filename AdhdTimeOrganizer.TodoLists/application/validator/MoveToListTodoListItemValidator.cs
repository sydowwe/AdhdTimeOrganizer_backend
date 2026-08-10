using AdhdTimeOrganizer.TodoLists.application.dto.request.todoList;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.TodoLists.application.validator;

public class MoveToListTodoListItemValidator : Validator<MoveToListTodoListItemRequest>
{
    public MoveToListTodoListItemValidator()
    {
        RuleFor(x => x.DestinationListId).GreaterThan(0);
    }
}