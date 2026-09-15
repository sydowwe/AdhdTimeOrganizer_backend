using FastEndpoints;
using FluentValidation;
using Sydowwe.Framework.application.dto.request.@base;
using Sydowwe.Framework.application.validator;
using Sydowwe.Framework.domain;

namespace AdhdTimeOrganizer.Core.application.validator;

public abstract class NameTextColorIconValidator<T> : Validator<T> where T : NameTextColorIconRequest
{
    protected NameTextColorIconValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(EntityFieldLengths.Name);

        // A palette name, never hex: the client's ColorPicker writes ColorPalette names and resolves
        // them to a theme-aware pair, and the seeders store the same names.
        RuleFor(x => x.Color)
            .MustBePaletteColor();

        RuleFor(x => x.Icon)
            .MaximumLength(EntityFieldLengths.Icon);

        RuleFor(x => x.Text)
            .MaximumLength(EntityFieldLengths.Text);
    }
}