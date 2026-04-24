using AdhdTimeOrganizer.application.dto.request.toDoList;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.application.validator;

public class CreateRoutineTodoListValidator : Validator<CreateRoutineTodoListRequest>
{
    public CreateRoutineTodoListValidator()
    {
        RuleFor(x => x.TotalCount)
            .InclusiveBetween(2, 99)
            .When(x => x.TotalCount.HasValue);

        RuleFor(x => x.Note)
            .MaximumLength(1000)
            .When(x => x.Note != null);

        RuleFor(x => x.SuggestedTime)
            .Must(t => t!.Hours is >= 0 and <= 23 && t.Minutes is >= 0 and <= 59)
            .When(x => x.SuggestedTime != null)
            .WithMessage("SuggestedTime hours must be 0–23 and minutes 0–59.");

        RuleFor(x => x.SuggestedDay)
            .InclusiveBetween(1, 30)
            .When(x => x.SuggestedDay.HasValue)
            .WithMessage("SuggestedDay must be between 1 and 30 (1–7 for weekly-aligned periods, 1–30 for monthly).");

        RuleForEach(x => x.Steps)
            .ChildRules(step =>
            {
                step.RuleFor(s => s.Name).MaximumLength(255);
                step.RuleFor(s => s.Note).MaximumLength(1000).When(s => s.Note != null);
            })
            .When(x => x.Steps != null);
    }
}
