using AdhdTimeOrganizer.ActivityProfiles.application.dto.request;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.ActivityProfiles.application.validator;

public class UpdateActivityProjectProfileValidator : Validator<ActivityProjectProfileRequest>
{
    public UpdateActivityProjectProfileValidator()
    {
        RuleFor(x => x.ProjectArea).NotEmpty().MaximumLength(255);
        RuleFor(x => x.EstimatedHours).GreaterThan(0);
    }
}