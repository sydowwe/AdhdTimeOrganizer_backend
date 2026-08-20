using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking.android;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.Tracking.application.validator;

public class AndroidStackedBarsValidator : Validator<AndroidStackedBarsRequest>
{
    public AndroidStackedBarsValidator()
    {
        this.ApplyDateRangeRules();
        RuleFor(x => x.WindowMinutes)
            .Must(DashboardDateRangeRules.AllowedWindowMinutes.Contains)
            .WithMessage(DashboardDateRangeRules.WindowMinutesMessage);
        RuleFor(x => x.MinSeconds)
            .GreaterThanOrEqualTo(0)
            .When(x => x.MinSeconds.HasValue);
    }
}
