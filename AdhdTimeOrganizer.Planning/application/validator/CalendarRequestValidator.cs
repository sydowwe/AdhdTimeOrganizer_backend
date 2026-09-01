using AdhdTimeOrganizer.Core.application.validator;
using AdhdTimeOrganizer.Planning.application.dto.request.taskPlanner;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.Planning.application.validator;

public class CalendarRequestValidator : Validator<CalendarRequest>
{
    public CalendarRequestValidator()
    {
        RuleFor(x => x.WakeUpTime).ApplyTimeOfDayRules();
        RuleFor(x => x.BedTime).ApplyTimeOfDayRules();

        RuleFor(x => x.Label)
            .MaximumLength(100)
            .When(x => x.Label != null);

        RuleFor(x => x.Weather)
            .MaximumLength(100)
            .When(x => x.Weather != null);

        RuleFor(x => x.Notes)
            .MaximumLength(1000)
            .When(x => x.Notes != null);
    }
}