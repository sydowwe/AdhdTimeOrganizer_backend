using AdhdTimeOrganizer.application.dto.request.user;
using FluentValidation;
using Sydowwe.Framework.application.validator;

namespace AdhdTimeOrganizer.application.validator;

public class UpdateUserPreferencesValidator : BaseUserPreferencesValidator<UpdateUserPreferencesRequest>
{
    public UpdateUserPreferencesValidator()
    {
        RuleFor(x => x.FirstDayOfWeek)
            .InclusiveBetween(0, 1)
            .When(x => x.FirstDayOfWeek.HasValue)
            .WithMessage("FirstDayOfWeek must be 0 (Sunday) or 1 (Monday).");

        // Length only. What makes a place name valid is whether the geocoder finds it, and that answer belongs
        // to a request the user should not have to wait on to save a setting — an unresolvable location simply
        // yields no weather signal. The empty string is deliberately allowed: it is how the field is cleared.
        RuleFor(x => x.WeatherLocation)
            .MaximumLength(120)
            .When(x => x.WeatherLocation is not null);
    }
}