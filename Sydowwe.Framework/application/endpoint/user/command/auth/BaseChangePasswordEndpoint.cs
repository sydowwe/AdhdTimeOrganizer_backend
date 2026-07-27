using FastEndpoints;
using Microsoft.AspNetCore.Identity;
using Sydowwe.Framework.application.dto.request.user;
using Sydowwe.Framework.application.middleware;
using Sydowwe.Framework.application.preprocessor;
using Sydowwe.Framework.domain.entity.user;
using Sydowwe.Framework.domain.extServiceContract.user.auth;
using Sydowwe.Framework.domain.helper;
using Sydowwe.Framework.domain.serviceContract;

namespace Sydowwe.Framework.application.endpoint.user.command.auth;

/// <summary>
/// Changes the current user's password.
///
/// <para>Gated by <see cref="VerifyUserPreProcessor{TUser,TRequest}"/>: the caller must re-supply the
/// current password AND a valid 2FA token before the handler runs. An authenticated session alone is
/// deliberately not enough — otherwise a hijacked session could rewrite the very credential needed to
/// take the account back.</para>
///
/// <para>On success every refresh token is revoked and all auth cookies are cleared, so the caller is
/// signed out and must log in with the new password. The alternative — silently minting a fresh
/// session so the user stays logged in — is rejected here: after a credential-reset event the safe
/// default is that no session survives, including this one.</para>
/// </summary>
public abstract class BaseChangePasswordEndpoint<TUser>(UserManager<TUser> userManager,
    IRefreshTokenService refreshTokenService)
    : Endpoint<ChangePasswordRequest>
    where TUser : BaseUser
{
    /// <summary>
    /// Revokes every outstanding session for the user. Abstract rather than an injected
    /// <c>IRefreshTokenService</c> so this endpoint stays decoupled from whichever refresh-token store
    /// the host runs — those contracts differ across solutions, and the password-change flow has no
    /// reason to depend on their shape.
    /// </summary>
    protected virtual Task RevokeAllSessionsAsync(TUser user, string ipAddress, CancellationToken ct) => refreshTokenService.RevokeAllUserTokensAsync(user.Id, ipAddress);

    public override void Configure()
    {
        Patch("user/password");
        PreProcessor<VerifyUserPreProcessor<TUser, ChangePasswordRequest>>();
        // ChangePasswordAsync does not feed the Identity lockout counter, so without this an
        // authenticated-but-hijacked session could brute-force the current password unbounded.
        Throttle(5, 60, TrustedIpMiddleware.ClientIpHeaderName);
        Summary(s =>
        {
            s.Summary = "Change the current user's password";
            s.Description = "Requires the current password and, when enabled, a 2FA token. " +
                            "Returns 401 if that verification fails, 400 if the new password is rejected. " +
                            "On success all sessions are revoked and the caller is signed out.";
        });
    }

    /// <summary>Override for solution-specific follow-up (e.g. clearing a MustChangePassword flag).</summary>
    protected virtual Task AfterPasswordChangedAsync(TUser user) => Task.CompletedTask;

    public override async Task HandleAsync(ChangePasswordRequest req, CancellationToken ct)
    {
        // Verified by the pre-processor — use that instance rather than re-querying, so the password
        // is changed on exactly the entity whose identity was proven.
        var user = HttpContext.GetVerifiedUser<TUser>();

        var result = await userManager.ChangePasswordAsync(user, req.Password, req.NewPassword);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                AddError(error.Description);
            // 400, not 401: identity was already proven by the pre-processor. Reaching here means the
            // NEW password was rejected (policy/complexity), which is a validation failure.
            await Send.ErrorsAsync(400, ct);
            return;
        }

        await AfterPasswordChangedAsync(user);

        // Credential-reset event: kill every refresh token, this session's included, so an older or
        // stolen session cannot outlive the change.
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        await RevokeAllSessionsAsync(user, ipAddress, ct);

        // Audit is optional infrastructure — hosts that haven't wired it up simply skip the row.
        // LogAndSaveAsync (not LogAsync) because this handler has no later SaveChanges to flush it.
        var auditService = TryResolve<IAuditService>();
        if (auditService is not null)
            await auditService.LogAndSaveAsync(
                "PasswordChanged", entityName: nameof(BaseUser), entityId: user.Id, ct: ct);

        HttpContext.Response.ClearSessionCookies();

        await Send.NoContentAsync(ct);
    }
}