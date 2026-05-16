using AdhdTimeOrganizer.application.dto.request.@base;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.application.validator;

public abstract class NameTextColorIconValidator<T> : Validator<T> where T : NameTextColorIconRequest
{
    protected NameTextColorIconValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(50);

        RuleFor(x => x.Color)
            .NotEmpty()
            .MaximumLength(7)
            .Matches("^#(?:[0-9a-fA-F]{3}){1,2}$")
            .WithMessage("Color must be a valid hex color code (e.g. #FFFFFF or #FFF)");

        RuleFor(x => x.Icon)
            .MaximumLength(255);

        RuleFor(x => x.Text)
            .MaximumLength(500);
    }
}