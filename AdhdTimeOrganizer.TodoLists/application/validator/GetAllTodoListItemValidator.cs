using AdhdTimeOrganizer.TodoLists.application.endpoint.todoList.todoListItem.query;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.TodoLists.application.validator;

/// <summary>
/// The list id is required, not optional: without it the read is every item the caller owns, with the whole
/// <c>Activity → Role → Category</c> graph and the owned steps JSON per row, and it grows without bound
/// (<c>PERF-1</c>). Rejecting the unfiltered call here is what keeps that read bounded to one list.
/// <para>
/// It is a 400 rather than a silent "all lists" default, because a default would be the same unbounded read
/// with nothing naming it. Ownership of the id is not checked — it does not need to be: the query is scoped
/// to the caller's items, so another user's list id answers an empty page and leaks nothing.
/// </para>
/// </summary>
public class GetAllTodoListItemValidator : Validator<GetAllTodoListItemRequest>
{
    public GetAllTodoListItemValidator()
    {
        RuleFor(x => x.TodoListId)
            .GreaterThan(0L)
            .WithMessage("TodoListId is required.");
    }
}
