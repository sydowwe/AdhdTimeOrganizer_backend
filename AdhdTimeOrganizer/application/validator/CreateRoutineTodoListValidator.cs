using AdhdTimeOrganizer.application.dto.request.toDoList;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AdhdTimeOrganizer.application.validator;

public class CreateRoutineTodoListValidator : Validator<CreateRoutineTodoListRequest>
{
    public CreateRoutineTodoListValidator(IServiceScopeFactory scopeFactory)
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

        RuleFor(x => x).CustomAsync(async (req, ctx, ct) =>
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var lengthInDays = await db.RoutineTimePeriods
                .Where(p => p.Id == req.TimePeriodId)
                .Select(p => (int?)p.LengthInDays)
                .FirstOrDefaultAsync(ct);

            if (lengthInDays is null) return;

            if (lengthInDays <= 1)
            {
                if (req.SuggestedDays.Count > 0)
                    ctx.AddFailure("SuggestedDays", "SuggestedDays must be empty for daily periods.");
                if (req.SuggestedDayOfMonth.HasValue)
                    ctx.AddFailure("SuggestedDayOfMonth", "SuggestedDayOfMonth must be null for daily periods.");
            }
            else if (lengthInDays <= 14)
            {
                if (req.SuggestedDayOfMonth.HasValue)
                    ctx.AddFailure("SuggestedDayOfMonth", "SuggestedDayOfMonth is only allowed for monthly+ periods (LengthInDays > 14).");
            }
            else
            {
                if (req.SuggestedDays.Count > 0)
                    ctx.AddFailure("SuggestedDays", "SuggestedDays is only allowed for weekly periods (LengthInDays ≤ 14).");
            }
        });

        RuleForEach(x => x.Steps)
            .ChildRules(step =>
            {
                step.RuleFor(s => s.Name).MaximumLength(255);
                step.RuleFor(s => s.Note).MaximumLength(1000).When(s => s.Note != null);
            })
            .When(x => x.Steps != null);
    }
}
