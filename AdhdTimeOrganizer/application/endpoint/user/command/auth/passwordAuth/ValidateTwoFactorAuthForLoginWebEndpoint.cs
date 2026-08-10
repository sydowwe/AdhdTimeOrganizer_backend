using AdhdTimeOrganizer.Core.domain.model.entity.user;
using Microsoft.AspNetCore.Identity;
using Sydowwe.Framework.application.endpoint.user.command.auth;
using Sydowwe.Framework.domain.extServiceContract.user.auth;
using Sydowwe.Framework.domain.serviceContract;

namespace AdhdTimeOrganizer.application.endpoint.user.command.auth.passwordAuth;

/// <summary>
/// Second step of the web login: reads the partial-auth token from the cookie and issues the real
/// session cookies. The base's defaults are exactly this, so only the throttling key is set here.
/// </summary>
public class ValidateTwoFactorAuthForLoginWebEndpoint(
    UserManager<User> userManager,
    ITwoFactorAuthService<User> twoFactorAuthService,
    IJwtService<User> jwtService,
    IAuditService auditService)
    : BaseValidateTwoFactorAuthForLoginEndpoint<User>(userManager, twoFactorAuthService, jwtService, auditService)
{
}