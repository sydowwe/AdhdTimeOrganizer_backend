using AdhdTimeOrganizer.TodoLists.application.dto.request.todoList;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.TodoLists.application.validator;

public class RescheduleOverdueTodoListItemValidator : Validator<RescheduleOverdueTodoListItemRequest>
{
    /// <summary>A year out is already a decision to drop the task; anything further is a typo.</summary>
    public const int MaxDays = 365;

    public RescheduleOverdueTodoListItemValidator()
    {
        RuleFor(x => x.Days).InclusiveBetween(0, MaxDays);
        RuleFor(x => x.ListId).GreaterThan(0).When(x => x.ListId is not null);
    }
}

/// <summary>
/// Its own type because FastEndpoints resolves validators by exact request type: without it the undo request would
/// be silently unvalidated.
/// </summary>
public class UndoRescheduleOverdueTodoListItemValidator : Validator<UndoRescheduleOverdueTodoListItemRequest>
{
    public UndoRescheduleOverdueTodoListItemValidator()
    {
        RuleFor(x => x.UndoToken).NotEmpty().MaximumLength(64);
    }
}
