using AdhdTimeOrganizer.domain.model.entity.user;
using Microsoft.AspNetCore.Identity;
using Sydowwe.Framework.application.endpoint.user.command.auth;
using Sydowwe.Framework.domain.extServiceContract.user.auth;
using Sydowwe.Framework.domain.serviceContract;

namespace AdhdTimeOrganizer.application.endpoint.user.command.auth;

/// <summary>
/// First-login 2FA provisioning for the web client: reads the partial-auth cookie written by the
/// password step and returns the QR + recovery codes. The base's defaults are exactly what the portal
/// needs, so nothing is overridden here.
/// <para>Web only — <c>ExtensionLoginEndpoint</c> carries the partial-auth token in the body rather
/// than a cookie, so the extension has no equivalent route yet.</para>
/// </summary>
public class SetupTwoFactorForLoginEndpoint(
    UserManager<User> userManager,
    ITwoFactorAuthService<User> twoFactorAuthService,
    IJwtService<User> jwtService,
    IAuditService auditService)
    : BaseSetupTwoFactorForLoginEndpoint<User>(userManager, twoFactorAuthService, jwtService, auditService)
{
}
