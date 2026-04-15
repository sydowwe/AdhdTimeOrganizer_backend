using AdhdTimeOrganizer.application.dto.request.activityTracking.android;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.application.validator;

public class AndroidStackedBarsValidator : Validator<AndroidStackedBarsRequest>
{
    private static readonly int[] ValidWindowMinutes = [15, 20, 30, 60, 90, 120];

    public AndroidStackedBarsValidator()
    {
        RuleFor(x => x.From).NotEmpty();
        RuleFor(x => x.WindowMinutes)
            .Must(x => ValidWindowMinutes.Contains(x))
            .WithMessage("WindowMinutes must be 15, 20, 30, 60, 90, 120");
        RuleFor(x => x.MinSeconds)
            .GreaterThanOrEqualTo(0)
            .When(x => x.MinSeconds.HasValue);
    }
}
