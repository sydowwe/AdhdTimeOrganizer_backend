using FastEndpoints;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Sydowwe.Framework.application.dto.request.user;
using Sydowwe.Framework.domain.entity.user;
using Sydowwe.Framework.domain.extServiceContract.user.auth;

namespace Sydowwe.Framework.application.preprocessor;

/// <summary>
/// Step-up authentication gate. Re-verifies the caller's password and, when 2FA is enabled, their
/// current TOTP token — before the endpoint handler runs. Attach to endpoints performing sensitive
/// operations (password change, email change, account deletion, 2FA reset) where an already
/// authenticated session is not sufficient proof of identity: a hijacked session must not be enough
/// to change the credentials that would let the attacker keep it.
///
/// <para>On success the verified user is stashed on <c>HttpContext.Items</c>; read it with
/// <see cref="VerifiedUserAccessor.GetVerifiedUser{TUser}"/> rather than re-querying, so the handler
/// operates on exactly the entity that was verified.</para>
/// </summary>
public class VerifyUserPreProcessor<TUser, TRequest> : IPreProcessor<TRequest>
    where TUser : BaseUser
    where TRequest : VerifyUserRequest
{
    public async Task PreProcessAsync(IPreProcessorContext<TRequest> context, CancellationToken ct)
    {
        // Resolve from the scoped context rather than the constructor: FastEndpoints instantiates
        // pre-processors once, so constructor-injected scoped services would be captured.
        var userManager = context.HttpContext.Resolve<UserManager<TUser>>();
        var twoFactorAuthService = context.HttpContext.Resolve<ITwoFactorAuthService<TUser>>();

        var user = await userManager.GetUserAsync(context.HttpContext.User);
        if (user is null)
        {
            await context.HttpContext.Response.SendUnauthorizedAsync(ct);
            return;
        }

        var req = context.Request!;

        var isPasswordValid = await userManager.CheckPasswordAsync(user, req.Password);
        if (!isPasswordValid)
        {
            context.ValidationFailures.Add(new ValidationFailure
            {
                PropertyName = nameof(req.Password),
                ErrorMessage = "Invalid password"
            });
            await context.HttpContext.Response.SendErrorsAsync(context.ValidationFailures, 401, cancellation: ct);
            return;
        }

        var twoFactorResult = await twoFactorAuthService.ValidateToken(user, req.TwoFactorAuthToken);
        if (twoFactorResult.Failed)
        {
            context.ValidationFailures.Add(new ValidationFailure
            {
                PropertyName = nameof(req.TwoFactorAuthToken),
                ErrorMessage = "Invalid two-factor authentication token"
            });
            await context.HttpContext.Response.SendErrorsAsync(context.ValidationFailures, 401, cancellation: ct);
            return;
        }

        context.HttpContext.Items[VerifiedUserAccessor.ItemsKey] = user;
    }
}

/// <summary>Reads the user stashed by <see cref="VerifyUserPreProcessor{TUser,TRequest}"/>.</summary>
public static class VerifiedUserAccessor
{
    public const string ItemsKey = "VerifiedUser";

    /// <summary>
    /// Gets the user that already passed password + 2FA verification for this request.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no verification ran — i.e. the endpoint forgot to register the pre-processor. This
    /// is deliberately fatal rather than a silent fallback to the ambient user: falling back would
    /// turn a missing step-up check into an unguarded sensitive operation.
    /// </exception>
    public static TUser GetVerifiedUser<TUser>(this HttpContext context) where TUser : BaseUser =>
        context.Items[ItemsKey] as TUser
        ?? throw new InvalidOperationException(
            "User not verified. Ensure VerifyUserPreProcessor is configured on this endpoint.");
}