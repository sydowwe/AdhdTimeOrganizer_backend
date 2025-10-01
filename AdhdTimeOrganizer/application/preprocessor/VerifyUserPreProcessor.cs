using AdhdTimeOrganizer.application.dto.request.user;
using AdhdTimeOrganizer.domain.extServiceContract.user.auth;
using AdhdTimeOrganizer.domain.model.entity.user;
using FastEndpoints;
using FluentValidation.Results;
using Microsoft.AspNetCore.Identity;

namespace AdhdTimeOrganizer.application.preprocessor;

/// <summary>
/// Pre-processor that verifies user identity via password and optionally 2FA token.
/// Attach this to endpoints that require user verification for sensitive operations.
/// </summary>
public class VerifyUserPreProcessor<TRequest> : IPreProcessor<TRequest>
    where TRequest : VerifyUserRequest
{
    public async Task PreProcessAsync(IPreProcessorContext<TRequest> context, CancellationToken ct)
    {
        // Resolve services from the scoped context instead of constructor
        var userManager = context.HttpContext.Resolve<UserManager<User>>();
        var twoFactorAuthService = context.HttpContext.Resolve<ITwoFactorAuthService>();

        var user = await userManager.GetUserAsync(context.HttpContext.User);
        if (user is null)
        {
            await context.HttpContext.Response.SendUnauthorizedAsync(ct);
            return;
        }

        var req = context.Request;

        // Verify password
        var isPasswordValid = await userManager.CheckPasswordAsync(user, req.Password);
        if (!isPasswordValid)
        {
            context.ValidationFailures.Add(new ValidationFailure()
            {
                PropertyName = nameof(req.Password),
                ErrorMessage = "Invalid password"
            });
            await context.HttpContext.Response.SendErrorsAsync(context.ValidationFailures, 401, cancellation: ct);
            return;
        }

        // Verify 2FA token if enabled
        var twoFactorResult = await twoFactorAuthService.ValidateToken(user, req.TwoFactorAuthToken);
        if (twoFactorResult.Failed)
        {
            context.ValidationFailures.Add(new ValidationFailure()
            {
                PropertyName = nameof(req.TwoFactorAuthToken),
                ErrorMessage = "Invalid two-factor authentication token"
            });
            await context.HttpContext.Response.SendErrorsAsync(context.ValidationFailures, 401, cancellation: ct);
            return;
        }

        // Store verified user in HttpContext for use in endpoint
        context.HttpContext.Items["VerifiedUser"] = user;
    }
}