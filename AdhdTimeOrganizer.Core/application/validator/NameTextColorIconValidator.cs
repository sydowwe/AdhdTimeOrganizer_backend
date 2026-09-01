using FastEndpoints;
using FluentValidation;
using Sydowwe.Framework.application.dto.request.@base;
using Sydowwe.Framework.domain;

namespace AdhdTimeOrganizer.Core.application.validator;

public abstract class NameTextColorIconValidator<T> : Validator<T> where T : NameTextColorIconRequest
{
    protected NameTextColorIconValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(EntityFieldLengths.Name);

        RuleFor(x => x.Color)
            .NotEmpty()
            .MaximumLength(EntityFieldLengths.Color)
            .Matches("^#(?:[0-9a-fA-F]{3}){1,2}$")
            .WithMessage("Color must be a valid hex color code (e.g. #FFFFFF or #FFF)");

        RuleFor(x => x.Icon)
            .MaximumLength(EntityFieldLengths.Icon);

        RuleFor(x => x.Text)
            .MaximumLength(EntityFieldLengths.Text);
    }
}