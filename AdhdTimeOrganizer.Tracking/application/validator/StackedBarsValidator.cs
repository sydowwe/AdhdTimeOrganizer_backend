using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.Tracking.application.validator;

/// <summary>
/// The stacked-bars rules, shared by all three sources — as <see cref="StackedBarsRequest"/> now is.
/// It was <c>WebExtensionSummaryValidator</c>, a name that matched neither the dashboard it validated
/// nor the two other dashboards that already bound it.
/// </summary>
public class StackedBarsValidator : Validator<StackedBarsRequest>
{
    public StackedBarsValidator()
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
