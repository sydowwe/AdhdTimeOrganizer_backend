using AdhdTimeOrganizer.application.dto.request.activityTracking;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.application.validator;

public class WebExtensionTimelineValidator : Validator<WebExtensionTimelineRequest>
{
    public WebExtensionTimelineValidator()
    {
        RuleFor(x => x.From).NotEmpty();
        RuleFor(x => x.MinSeconds)
            .InclusiveBetween(0, 3600)
            .When(x => x.MinSeconds.HasValue);
    }
}
