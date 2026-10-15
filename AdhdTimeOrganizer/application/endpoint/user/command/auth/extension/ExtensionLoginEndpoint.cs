using AdhdTimeOrganizer.Core.domain.model.entity.user;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Sydowwe.Framework.application.endpoint.user.command.auth;
using Sydowwe.Framework.config;
using Sydowwe.Framework.domain.extServiceContract.user.auth;
using Sydowwe.Framework.domain.result;
using Sydowwe.Framework.domain.serviceContract;

namespace AdhdTimeOrganizer.application.endpoint.user.command.auth.extension;

/// <summary>
/// Extension (token) login. Password check, lockout, email confirmation, the 2FA branch and the token
/// pair all come from the framework base.
///
/// <para>Every account may sign a tracker in (FO7, decided 2026-10-06). The per-account flag added no security:
/// the tracker signs in with the account's own password through the same flow as the web, and the
/// <c>ActivityTracking</c> role still comes from the token's client type alone. The unread
/// <c>User.HasExtensionAccess</c> column is gone (migration <c>DropUserHasExtensionAccess</c>). Pairing codes for
/// trackers were considered and rejected on 2026-10-12: an account signed up through Google sets a password through
/// Forgot password.</para>
///
/// <para>The one exception is an account scheduled for deletion (P3-07), refused by <see cref="DenyAccountScheduledForDeletion"/>:
/// a tracker cannot show the "keep my account?" step a web sign-in does, and it must not quietly go on feeding data
/// into an account that is about to be erased.</para>
/// </summary>
public class ExtensionLoginEndpoint(
    UserManager<User> userManager,
    IJwtService<User> jwtService,
    IAuditService auditService,
    IOptions<TwoFactorOptions> twoFactorOptions)
    : BaseExtensionLoginEndpoint<User>(userManager, jwtService, auditService, twoFactorOptions)
{
    protected override ExtensionAccessDenial? DenyExtensionAccess(User user) => DenyAccountScheduledForDeletion(user);

    /// <summary>Shared with the 2FA step, which issues the session when the password step only asked for a code.</summary>
    public static ExtensionAccessDenial? DenyAccountScheduledForDeletion(User user) =>
        user.DeletionScheduledFor is null
            ? null
            : new ExtensionAccessDenial(
                "This account is scheduled for deletion. Sign in to the web app to keep it.",
                ResultErrorType.AccountDeletionScheduled);
}
