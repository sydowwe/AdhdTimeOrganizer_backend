using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking.android;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.Tracking.application.validator;

public class AndroidTimelineValidator : Validator<AndroidTimelineRequest>
{
    public AndroidTimelineValidator()
    {
        this.ApplyDateRangeRules();
        this.ApplySingleDayRule();
        RuleFor(x => x.MinSeconds)
            .GreaterThanOrEqualTo(0)
            .When(x => x.MinSeconds.HasValue);
    }
}
