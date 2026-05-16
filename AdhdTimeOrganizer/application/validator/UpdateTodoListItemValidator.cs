using AdhdTimeOrganizer.application.dto.request.todoList;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.application.validator;

public class UpdateTodoListItemValidator : Validator<UpdateTodoListItemRequest>
{
    public UpdateTodoListItemValidator()
    {
        RuleFor(x => x.TaskPriorityId)
            .GreaterThan(0L);

        RuleFor(x => x.TotalCount)
            .InclusiveBetween(2, 99)
            .When(x => x.TotalCount.HasValue);

        RuleFor(x => x.DoneCount)
            .GreaterThanOrEqualTo(0)
            .When(x => x.DoneCount.HasValue);

        RuleFor(x => x)
            .Must(x => x.DoneCount <= x.TotalCount)
            .When(x => x.DoneCount.HasValue && x.TotalCount.HasValue)
            .WithMessage("DoneCount must not exceed TotalCount.")
            .OverridePropertyName(nameof(UpdateTodoListItemRequest.DoneCount));

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
