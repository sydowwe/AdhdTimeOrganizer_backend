using AdhdTimeOrganizer.application.dto.request.activity.profile;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.application.validator;

public class UpdateActivityProjectProfileValidator : Validator<ActivityProjectProfileRequest>
{
    public UpdateActivityProjectProfileValidator()
    {
        RuleFor(x => x.ProjectArea).NotEmpty().MaximumLength(255);
        RuleFor(x => x.EstimatedHours).GreaterThan(0);
    }
}
