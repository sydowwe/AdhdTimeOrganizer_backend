using AdhdTimeOrganizer.TodoLists.application.dto.request.todoList;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.TodoLists.application.validator;

public class UpdateTodoListItemValidator : Validator<UpdateTodoListItemRequest>
{
    public UpdateTodoListItemValidator()
    {
        RuleFor(x => x.TaskPriorityId)
            .GreaterThan(0L);

        RuleFor(x => x.TotalCount)
            .InclusiveBetween(2, 99)
            .When(x => x.TotalCount.HasValue);

        // No DoneCount rules: the request no longer carries one (see BaseUpdateTodoListRequest) — the
        // endpoint's AfterMapping derives the count from TotalCount, so there is nothing here to bound.

        RuleFor(x => x.PairedLeisureActivityId)
            .GreaterThan(0L)
            .When(x => x.PairedLeisureActivityId.HasValue);

        // Same rule as on create — see CreateTodoListItemValidator for why existence and
        // leisure-ness are deliberately not checked here.
        RuleFor(x => x.PairedLeisureActivityId)
            .NotEqual(x => (long?)x.ActivityId)
            .When(x => x.PairedLeisureActivityId.HasValue)
            .WithMessage("A task cannot be paired with its own activity.");

        RuleFor(x => x.Note)
            .MaximumLength(1000)
            .When(x => x.Note != null);

        RuleFor(x => x.SuggestedTime)
            .Must(t => t!.Hours is >= 0 and <= 23 && t.Minutes is >= 0 and <= 59)
            .When(x => x.SuggestedTime != null)
            .WithMessage("SuggestedTime hours must be 0–23 and minutes 0–59.");

        RuleForEach(x => x.Steps)
            .ChildRules(step =>
            {
                step.RuleFor(s => s.Name).MaximumLength(255);
                step.RuleFor(s => s.Note).MaximumLength(1000).When(s => s.Note != null);
            })
            .When(x => x.Steps != null);
    }
}