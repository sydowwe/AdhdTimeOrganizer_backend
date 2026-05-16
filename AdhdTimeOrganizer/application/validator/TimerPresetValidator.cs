using AdhdTimeOrganizer.application.dto.request.timer;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.application.validator;

public class TimerPresetValidator : Validator<TimerPresetRequest>
{
    public TimerPresetValidator()
    {
        RuleFor(x => x.Duration)
            .GreaterThan(0);

        RuleFor(x => x.ActivityId)
            .GreaterThan(0L)
            .When(x => x.ActivityId.HasValue);
    }
}
