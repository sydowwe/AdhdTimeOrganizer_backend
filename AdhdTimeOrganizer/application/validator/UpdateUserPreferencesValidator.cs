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
    }
}
