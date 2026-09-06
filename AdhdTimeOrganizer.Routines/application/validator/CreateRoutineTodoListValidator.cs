using AdhdTimeOrganizer.Routines.application.dto.request.todoList;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.Routines.application.validator;

public class CreateRoutineTodoListValidator : Validator<CreateRoutineTodoListRequest>
{
    public CreateRoutineTodoListValidator(IServiceScopeFactory scopeFactory, IHttpContextAccessor http)
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

        RuleForEach(x => x.SuggestedDays)
            .IsInEnum()
            .WithMessage("Each SuggestedDays value must be a valid DayOfWeek (0–6).");

        RuleFor(x => x.SuggestedDayOfMonth)
            .InclusiveBetween(1, 31)
            .When(x => x.SuggestedDayOfMonth.HasValue)
            .WithMessage("SuggestedDayOfMonth must be between 1 and 31.");

        RuleFor(x => x).CustomAsync((req, ctx, ct) => RoutineTodoListPeriodRules.ApplyAsync(
            ctx, req.TimePeriodId, req.SuggestedDays, req.SuggestedDayOfMonth, scopeFactory, http, ct));

        RuleForEach(x => x.Steps)
            .ChildRules(step =>
            {
                step.RuleFor(s => s.Name).MaximumLength(255);
                step.RuleFor(s => s.Note).MaximumLength(1000).When(s => s.Note != null);
            })
            .When(x => x.Steps != null);
    }
}