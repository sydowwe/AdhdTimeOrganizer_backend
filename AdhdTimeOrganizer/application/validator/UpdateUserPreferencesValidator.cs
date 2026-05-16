using AdhdTimeOrganizer.application.dto.request.user;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.application.validator;

public class UpdateUserPreferencesValidator : Validator<UpdateUserPreferencesRequest>
{
    public UpdateUserPreferencesValidator()
    {
        RuleFor(x => x.FirstDayOfWeek)
            .InclusiveBetween(0, 1)
            .When(x => x.FirstDayOfWeek.HasValue)
            .WithMessage("FirstDayOfWeek must be 0 (Sunday) or 1 (Monday).");

        RuleFor(x => x.Timezone)
            .Must(BeValidTimezone!)
            .When(x => x.Timezone != null)
            .WithMessage("Timezone must be a valid IANA or system timezone identifier.");

        RuleFor(x => x.Theme)
            .IsInEnum()
            .When(x => x.Theme.HasValue);

        RuleFor(x => x.Locale)
            .IsInEnum()
            .When(x => x.Locale.HasValue);
    }

    private static bool BeValidTimezone(string timezone)
    {
        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(timezone);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
    }
}
