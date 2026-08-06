using AdhdTimeOrganizer.domain.model.entity.user;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Sydowwe.Framework.application.endpoint.user.command.auth;
using Sydowwe.Framework.config;
using Sydowwe.Framework.domain.extServiceContract.user.auth;
using Sydowwe.Framework.domain.serviceContract;

namespace AdhdTimeOrganizer.application.endpoint.user.command.auth.passwordAuth;

/// <summary>
/// Web (cookie) login. Everything — password check, lockout, email confirmation, the 2FA branch and
/// the audit trail — lives in the framework base; this class only pins the route and the throttling
/// key this deployment uses.
/// </summary>
public class LoginUserEndpoint(
    SignInManager<User> signInManager,
    UserManager<User> userManager,
    IJwtService<User> jwtService,
    IGoogleRecaptchaService googleRecaptchaService,
    IAuditService auditService,
    IOptions<TwoFactorOptions> twoFactorOptions)
    : BaseLoginEndpoint<User>(signInManager, userManager, jwtService, googleRecaptchaService, auditService, twoFactorOptions)
{
    // Nothing to configure: route, AllowAnonymous, throttling (keyed on the trusted client-IP header),
    // the 2FA branch and auditing all come from the base.
}