using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.Tracking.application.validator;

public class BaseTimelineValidator : Validator<BaseTimelineRequest>
{
    public BaseTimelineValidator()
    {
        this.ApplyDateRangeRules();
        this.ApplySingleDayRule();
        RuleFor(x => x.MinSeconds)
            .InclusiveBetween(0, 3600)
            .When(x => x.MinSeconds.HasValue);
    }
}
