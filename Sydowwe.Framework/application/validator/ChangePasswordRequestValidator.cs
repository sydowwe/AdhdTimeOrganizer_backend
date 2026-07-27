using FastEndpoints;
using FluentValidation;
using Sydowwe.Framework.application.dto.request.user;

namespace Sydowwe.Framework.application.validator;

public class ChangePasswordRequestValidator : Validator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        // The current password arrives as VerifyUserRequest.Password - see ChangePasswordRequest.
        RuleFor(x => x.Password)
            .NotEmpty()
            .WithMessage("Current password is required.");

        RuleFor(x => x.NewPassword)
            .NotEmpty()
            .WithMessage("New password is required.")
            .NotEqual(x => x.Password)
            .WithMessage("New password must be different from the current password.");
    }
}