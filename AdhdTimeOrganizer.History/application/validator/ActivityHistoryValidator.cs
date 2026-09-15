using AdhdTimeOrganizer.History.application.dto.request.history;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.History.application.validator;

public class ActivityHistoryValidator : Validator<ActivityHistoryRequest>
{
    public ActivityHistoryValidator()
    {
        RuleFor(x => x.ActivityId)
            .NotEmpty();

        RuleFor(x => x.StartTimestamp)
            .NotEmpty();

        // Cascade(Stop) is load-bearing, not tidiness: Length is a non-nullable reference the client can
        // still send as JSON null, and the default rule-level cascade runs Must on the null, which throws
        // inside validation and answers 500 instead of the 400 the NotNull is there to produce.
        //
        // Strictly positive, not non-negative: a recording of nothing is not information on any read path
        // (the dashboards sum it, the timeline draws a zero-width block, the calibration average divides
        // by it). The SPA's capture views discard sub-threshold sessions before they reach here; this is
        // the backstop for every other client. IntTime is whole minutes, so > 0 is a one-minute floor.
        RuleFor(x => x.Length)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .Must(x => x.TotalSeconds > 0)
            .WithMessage("Length must be positive.");

        RuleFor(x => x.TodoListItemId)
            .GreaterThan(0)
            .When(x => x.TodoListItemId.HasValue);

        RuleFor(x => x.RoutineTodoListId)
            .GreaterThan(0)
            .When(x => x.RoutineTodoListId.HasValue);

        // One recording is raised by completing one item. Both set would mean the same seconds were
        // claimed by a to-do item and a routine item at once, which no prompt can produce and which
        // would double-count the row across two recaps.
        RuleFor(x => x)
            .Must(x => !(x.TodoListItemId.HasValue && x.RoutineTodoListId.HasValue))
            .WithMessage("A recording cannot be linked to both a to-do item and a routine item.")
            .WithName(nameof(ActivityHistoryRequest.TodoListItemId));
    }
}