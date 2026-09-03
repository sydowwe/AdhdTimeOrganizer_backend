using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking.desktop;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.Tracking.application.validator;

public class DesktopProcessDetailsValidator : Validator<DesktopProcessDetailsRequest>
{
    public DesktopProcessDetailsValidator()
    {
        this.ApplyDetailsDateRangeRules();
        RuleFor(x => x.ProcessName).NotEmpty().MaximumLength(TrackingIngestLimits.MaxProcessNameLength);
    }
}
