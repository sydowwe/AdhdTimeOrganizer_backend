using AdhdTimeOrganizer.application.dto.request.taskPlanner;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.application.validator;

public class CalendarRequestValidator : Validator<CalendarRequest>
{
    public CalendarRequestValidator()
    {
        RuleFor(x => x.WakeUpTime)
            .Must(t => t.Hours is >= 0 and <= 23 && t.Minutes is >= 0 and <= 59)
            .WithMessage("WakeUpTime hours must be 0–23 and minutes 0–59.");

        RuleFor(x => x.BedTime)
            .Must(t => t.Hours is >= 0 and <= 23 && t.Minutes is >= 0 and <= 59)
            .WithMessage("BedTime hours must be 0–23 and minutes 0–59.");

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
