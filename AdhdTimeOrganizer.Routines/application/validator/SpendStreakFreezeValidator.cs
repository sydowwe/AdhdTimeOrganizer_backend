using AdhdTimeOrganizer.Routines.application.dto.request.todoList;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.Routines.application.validator;

/// <summary>
/// Shape only. Whether the named period is missed, already frozen or affordable are domain rules evaluated
/// against the stored period by <c>RoutineStreakFreezeService</c> — none of them are decidable from the body.
/// </summary>
public class SpendStreakFreezeValidator : Validator<SpendStreakFreezeRequest>
{
    public SpendStreakFreezeValidator()
    {
        RuleFor(x => x.PeriodStart)
            .NotEmpty()
            .Must(x => SpendStreakFreezeRequest.TryParse(x, out _))
            .WithMessage("PeriodStart must be an ISO-8601 date or instant.");
    }
}
