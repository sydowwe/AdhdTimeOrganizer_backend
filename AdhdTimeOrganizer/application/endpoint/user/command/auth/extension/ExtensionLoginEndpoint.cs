using AdhdTimeOrganizer.Core.domain.model.entity.user;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Sydowwe.Framework.application.endpoint.user.command.auth;
using Sydowwe.Framework.config;
using Sydowwe.Framework.domain.extServiceContract.user.auth;
using Sydowwe.Framework.domain.serviceContract;

namespace AdhdTimeOrganizer.application.endpoint.user.command.auth.extension;

/// <summary>
/// Extension (token) login. Password check, lockout, email confirmation, the 2FA branch and the token
/// pair all come from the framework base.
///
/// <para>There is deliberately no <c>HasExtensionAccess</c> override, so every account may sign a tracker
/// in (FO7, decided 2026-10-06). The per-account flag added no security: the tracker signs in with the
/// account's own password through the same flow as the web, and the <c>ActivityTracking</c> role still
/// comes from the token's client type alone. The unread <c>User.HasExtensionAccess</c> column is gone
/// (migration <c>DropUserHasExtensionAccess</c>). Pairing codes for trackers were considered and rejected on
/// 2026-10-12: an account signed up through Google sets a password through Forgot password.</para>
/// </summary>
public class ExtensionLoginEndpoint(
    UserManager<User> userManager,
    IJwtService<User> jwtService,
    IAuditService auditService,
    IOptions<TwoFactorOptions> twoFactorOptions)
    : BaseExtensionLoginEndpoint<User>(userManager, jwtService, auditService, twoFactorOptions);
