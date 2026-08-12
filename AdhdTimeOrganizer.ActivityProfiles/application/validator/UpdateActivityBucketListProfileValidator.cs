using AdhdTimeOrganizer.ActivityProfiles.application.dto.request;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.ActivityProfiles.application.validator;

public class UpdateActivityBucketListProfileValidator : Validator<ActivityBucketListProfileRequest>
{
    public UpdateActivityBucketListProfileValidator()
    {
        RuleFor(x => x.ComfortZoneStep).InclusiveBetween(1, 10);
        RuleFor(x => x.InspirationSource).NotEmpty().MaximumLength(500);
        RuleFor(x => x.FinancialGoal).GreaterThan(0).When(x => x.FinancialGoal.HasValue);
    }
}