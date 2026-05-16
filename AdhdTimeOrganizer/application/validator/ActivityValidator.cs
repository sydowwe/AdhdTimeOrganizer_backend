using AdhdTimeOrganizer.application.dto.request.activity;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.application.validator;

public class ActivityValidator : Validator<ActivityRequest>
{
    public ActivityValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(50);

        RuleFor(x => x.Text)
            .MaximumLength(500);

        RuleFor(x => x.RoleId)
            .NotEmpty();
    }
}
