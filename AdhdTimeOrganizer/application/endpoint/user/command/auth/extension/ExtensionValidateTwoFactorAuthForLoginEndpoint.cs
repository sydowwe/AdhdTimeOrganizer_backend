using AdhdTimeOrganizer.Core.domain.model.entity.user;
using FastEndpoints;
using Microsoft.AspNetCore.Identity;
using Sydowwe.Framework.application.dto.request.user;
using Sydowwe.Framework.application.dto.response.user;
using Sydowwe.Framework.application.endpoint.user.command.auth;
using Sydowwe.Framework.application.error;
using Sydowwe.Framework.domain.@enum;
using Sydowwe.Framework.domain.extServiceContract.user.auth;
using Sydowwe.Framework.domain.serviceContract;

namespace AdhdTimeOrganizer.application.endpoint.user.command.auth.extension;

/// <summary>
/// Second step of the extension login. Same validation as the web endpoint — the framework base owns
/// the single-use guard, lockout and recovery codes — but both ends are token-based rather than
/// cookie-based: the pending token arrives in the request body, and the real session is handed back
/// as a token pair instead of being written to cookies an extension can't use.
/// </summary>
public class ValidateTwoFactorAuthForLoginExtensionEndpoint(
    ITwoFactorAuthService<User> twoFactorAuthService,
    IJwtService<User> jwtService,
    IAuditService auditService)
    : BaseValidateTwoFactorAuthForLoginEndpoint<User>(twoFactorAuthService, jwtService, auditService)
{
    private readonly IJwtService<User> _jwtService = jwtService;

    public override void Configure()
    {
        // Only the route genuinely differs — this is the token-based sibling of /auth/login/2fa.
        base.Configure();
        Post("auth/login/2fa/extension");
        Summary(s => { s.Summary = "Validate 2FA for login (extension — body token)"; });
    }

    protected override string? ReadPendingToken(TwoFactorAuthLoginRequest request)
    {
        return request.PendingAuthToken;
    }

    protected override async Task OnAuthenticatedAsync(User user, TwoFactorAuthLoginRequest request, CancellationToken ct)
    {
        // The password step refuses an account scheduled for deletion, but a code can arrive after a deletion was
        // scheduled in between (the pending token lives for minutes), so the step that issues the session asks too.
        if (ExtensionLoginEndpoint.DenyAccountScheduledForDeletion(user) is { } denial)
        {
            AddError(denial.Message);
            await Send.ErrorsAsync(denial.ErrorType, 403, ct);
            return;
        }

        var (accessToken, refreshToken) = await _jwtService.GenerateTokensForExtensionAsync(AuthMethodEnum.Password, user);

        await HttpContext.Response.SendAsync(new ExtensionLoginResponse
        {
            RequiresTwoFactor = false,
            AccessToken = accessToken,
            RefreshToken = refreshToken
        }, cancellation: ct);
    }
}