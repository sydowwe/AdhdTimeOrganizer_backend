using FastEndpoints;
using FluentValidation;
using Sydowwe.Framework.application.dto.request.user;

namespace Sydowwe.Framework.application.validator;

/// <summary>
/// Rules for the <see cref="UserPreferencesRequest"/> fields. Generic over the request so a host that
/// adds preference columns derives this and adds rules for its own fields without restating these.
/// </summary>
public abstract class BaseUserPreferencesValidator<TRequest> : Validator<TRequest>
    where TRequest : UserPreferencesRequest
{
    protected BaseUserPreferencesValidator()
    {
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