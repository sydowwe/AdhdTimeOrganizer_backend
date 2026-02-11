using AdhdTimeOrganizer.application.dto.request;
using AdhdTimeOrganizer.application.dto.request.activityTracking;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.application.validator;

public class WebExtensionSummaryValidator : Validator<WebExtensionStackedBarsRequest>
{
    private static readonly int[] ValidWindowMinutes = [15, 20, 30, 60, 90, 120];

    public WebExtensionSummaryValidator()
    {
        RuleFor(x => x.From).NotEmpty();
        RuleFor(x => x.WindowMinutes)
            .Must(x => ValidWindowMinutes.Contains(x))
            .WithMessage("WindowMinutes must be 15, 20, 30, 60, 90, 120");
    }
}