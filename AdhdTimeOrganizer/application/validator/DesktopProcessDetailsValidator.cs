using AdhdTimeOrganizer.application.dto.request.activityTracking.desktop;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.application.validator;

public class DesktopProcessDetailsValidator : Validator<DesktopProcessDetailsRequest>
{
    public DesktopProcessDetailsValidator()
    {
        RuleFor(x => x.From).NotEmpty();
        RuleFor(x => x.To).NotEmpty().GreaterThan(x => x.From);
        RuleFor(x => x.ProcessName).NotEmpty();
    }
}
