using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.Tracking.application.validator;

public class WebExtensionSummaryValidator : Validator<WebExtensionStackedBarsRequest>
{
    public WebExtensionSummaryValidator()
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
