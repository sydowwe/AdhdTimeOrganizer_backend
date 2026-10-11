using AdhdTimeOrganizer.Planning.application.dto.request.taskPlanner;
using AdhdTimeOrganizer.Planning.domain.model.@enum;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.Planning.application.validator;

public class DismissSuggestionValidator : Validator<DismissSuggestionRequest>
{
    public DismissSuggestionValidator()
    {
        // Ownership needs the database, so it is checked in the endpoint.
        RuleFor(x => x.ActivityId).GreaterThan(0);

        RuleFor(x => x.PatternType)
            .Must(t => t is RecurrenceType.DayOfWeek or RecurrenceType.DayOfMonth)
            .WithMessage("Only a DayOfWeek or DayOfMonth pattern can be dismissed; a user-set recurrence is deleted instead.");

        RuleFor(x => x.PatternValue).InclusiveBetween(1, 7).When(x => x.PatternType == RecurrenceType.DayOfWeek);
        RuleFor(x => x.PatternValue).InclusiveBetween(1, 31).When(x => x.PatternType == RecurrenceType.DayOfMonth);
    }
}
