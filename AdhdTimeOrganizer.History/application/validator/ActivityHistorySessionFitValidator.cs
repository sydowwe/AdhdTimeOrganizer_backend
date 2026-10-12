using AdhdTimeOrganizer.History.application.dto.request.history;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.History.application.validator;

public class ActivityHistorySessionFitValidator : Validator<ActivityHistorySessionFitRequest>
{
    public ActivityHistorySessionFitValidator()
    {
        RuleFor(x => x.ActivityId)
            .GreaterThan(0)
            .WithMessage("activityId is required.");
    }
}
