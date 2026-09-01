using AdhdTimeOrganizer.Core.application.dto.request.activity;
using FastEndpoints;
using FluentValidation;
using Sydowwe.Framework.domain;

namespace AdhdTimeOrganizer.Core.application.validator;

public class ActivityValidator : Validator<ActivityRequest>
{
    public ActivityValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(EntityFieldLengths.Name);

        RuleFor(x => x.Text)
            .MaximumLength(EntityFieldLengths.Text);

        RuleFor(x => x.RoleId)
            .NotEmpty();
    }
}