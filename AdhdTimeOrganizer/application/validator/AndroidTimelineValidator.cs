using AdhdTimeOrganizer.application.dto.request.activityTracking.android;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.application.validator;

public class AndroidTimelineValidator : Validator<AndroidTimelineRequest>
{
    public AndroidTimelineValidator()
    {
        RuleFor(x => x.From).NotEmpty();
        RuleFor(x => x.MinSeconds)
            .GreaterThanOrEqualTo(0)
            .When(x => x.MinSeconds.HasValue);
    }
}
