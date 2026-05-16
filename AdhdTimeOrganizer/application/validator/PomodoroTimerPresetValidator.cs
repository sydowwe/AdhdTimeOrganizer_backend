using AdhdTimeOrganizer.application.dto.request.timer;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.application.validator;

public class PomodoroTimerPresetValidator : Validator<PomodoroTimerPresetRequest>
{
    public PomodoroTimerPresetValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(255);

        RuleFor(x => x.FocusDuration)
            .GreaterThan(0);

        RuleFor(x => x.ShortBreakDuration)
            .GreaterThan(0);

        RuleFor(x => x.LongBreakDuration)
            .GreaterThan(0);

        RuleFor(x => x.FocusPeriodInCycleCount)
            .GreaterThan(0);

        RuleFor(x => x.NumberOfCycles)
            .GreaterThan(0);

        RuleFor(x => x.FocusActivityId)
            .GreaterThan(0L)
            .When(x => x.FocusActivityId.HasValue);

        RuleFor(x => x.RestActivityId)
            .GreaterThan(0L)
            .When(x => x.RestActivityId.HasValue);
    }
}
