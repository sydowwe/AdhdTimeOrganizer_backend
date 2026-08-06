using AdhdTimeOrganizer.domain.model.entity.user;
using FastEndpoints;
using Microsoft.AspNetCore.Identity;
using Sydowwe.Framework.application.dto.request.user;
using Sydowwe.Framework.application.dto.response.user;
using Sydowwe.Framework.application.endpoint.user.command.auth;
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
    UserManager<User> userManager,
    ITwoFactorAuthService<User> twoFactorAuthService,
    IJwtService<User> jwtService,
    IAuditService auditService)
    : BaseValidateTwoFactorAuthForLoginEndpoint<User>(userManager, twoFactorAuthService, jwtService, auditService)
{
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
        var (accessToken, refreshToken) = await jwtService.GenerateTokensForExtensionAsync(AuthMethodEnum.Password, user);

        await HttpContext.Response.SendAsync(new ExtensionLoginResponse
        {
            RequiresTwoFactor = false,
            AccessToken = accessToken,
            RefreshToken = refreshToken
        }, cancellation: ct);
    }
}