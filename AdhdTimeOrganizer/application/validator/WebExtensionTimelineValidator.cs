using AdhdTimeOrganizer.application.dto.request.activityTracking;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.application.validator;

public class WebExtensionTimelineValidator : Validator<WebExtensionTimelineRequest>
{
    public WebExtensionTimelineValidator()
    {
        RuleFor(x => x.From).NotEmpty();
        RuleFor(x => x.To).NotEmpty().GreaterThan(x => x.From);
        RuleFor(x => x.To).LessThanOrEqualTo(x => x.From.AddDays(7))
            .WithMessage("Maximum range is 7 days for timeline view");
        RuleFor(x => x.MinSeconds)
            .InclusiveBetween(0, 3600)
            .When(x => x.MinSeconds.HasValue);
    }
}
