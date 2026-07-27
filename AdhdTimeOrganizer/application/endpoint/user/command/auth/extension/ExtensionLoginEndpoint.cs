using AdhdTimeOrganizer.domain.model.entity.user;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Sydowwe.Framework.application.endpoint.user.command.auth;
using Sydowwe.Framework.config;
using Sydowwe.Framework.domain.extServiceContract.user.auth;
using Sydowwe.Framework.domain.serviceContract;

namespace AdhdTimeOrganizer.application.endpoint.user.command.auth.extension;

/// <summary>
/// Extension (token) login. Password check, lockout, email confirmation, the 2FA branch and the token
/// pair all come from the framework base; the only thing this deployment adds is the per-account
/// extension gate.
/// </summary>
public class ExtensionLoginEndpoint(
    SignInManager<User> signInManager,
    UserManager<User> userManager,
    IJwtService<User> jwtService,
    IAuditService auditService,
    IOptions<TwoFactorOptions> twoFactorOptions)
    : BaseExtensionLoginEndpoint<User>(signInManager, userManager, jwtService, auditService, twoFactorOptions)
{
    // This portal gates extension access per account; the framework default lets everyone in.
    protected override bool HasExtensionAccess(User user) => user.HasExtensionAccess;
}
