using AdhdTimeOrganizer.application.dto.request.activityTracking;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.application.validator;

public class BaseTimelineValidator : Validator<BaseTimelineRequest>
{
    public BaseTimelineValidator()
    {
        RuleFor(x => x.From).NotEmpty();
        RuleFor(x => x.MinSeconds)
            .InclusiveBetween(0, 3600)
            .When(x => x.MinSeconds.HasValue);
    }
}
