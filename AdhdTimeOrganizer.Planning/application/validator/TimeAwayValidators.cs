using AdhdTimeOrganizer.Core.domain.model.@enum;
using AdhdTimeOrganizer.Planning.application.dto.filter;
using AdhdTimeOrganizer.Planning.application.dto.request.taskPlanner;
using AdhdTimeOrganizer.Planning.application.helper;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.Planning.application.validator;

/// <summary>
/// The shape rules of a stretch of time away. What a validator cannot see — that the stretch overlaps another of
/// the user's, or that the template is theirs — is checked by the write endpoints (<c>TimeAwayWriteRules</c>).
/// </summary>
public class TimeAwayValidator : Validator<TimeAwayRequest>
{
    public TimeAwayValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);

        RuleFor(x => x.Kind)
            .Must(kind => kind.IsAway())
            .WithMessage("Kind must be Vacation or SickDay.");

        RuleFor(x => x.EndDate)
            .GreaterThanOrEqualTo(x => x.StartDate)
            .WithMessage("EndDate must be on or after StartDate.");

        // The stamp touches every day of the range in one transaction, so the range is the cost, and it shares the
        // ceiling every other per-day calendar write has.
        RuleFor(x => x)
            .Must(x => x.EndDate.DayNumber - x.StartDate.DayNumber + 1 <= PlannerBatchLimits.MaxCalendarIds)
            .WithMessage($"A stretch of time away may not exceed {PlannerBatchLimits.MaxCalendarIds} days.")
            .When(x => x.EndDate >= x.StartDate);

        RuleFor(x => x.TemplateId).GreaterThan(0).When(x => x.TemplateId.HasValue);
        RuleFor(x => x.Location).IsInEnum().When(x => x.Location.HasValue);
        RuleFor(x => x.Notes).MaximumLength(1000).When(x => x.Notes != null);
    }
}

public class TimeAwayFilterValidator : Validator<TimeAwayFilter>
{
    public TimeAwayFilterValidator()
    {
        RuleFor(x => x)
            .Must(x => x.Until >= x.From)
            .WithMessage("Until must be on or after From.")
            .When(x => x.From.HasValue && x.Until.HasValue);
    }
}
