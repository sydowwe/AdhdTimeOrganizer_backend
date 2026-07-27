using AdhdTimeOrganizer.domain.model.entity.user;
using Sydowwe.Framework.application.preprocessor;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.application.helper;

/// <summary>
/// Portal-side conveniences over the framework's role groups and verified-user accessor. Named
/// <c>Portal…</c> to avoid colliding with <see cref="Sydowwe.Framework.domain.helper.EndpointHelper"/>,
/// which is a different helper entirely (the result-error → HTTP status map).
/// </summary>
public static class PortalEndpointHelper
{
    /// <summary>
    /// Non-generic convenience over <see cref="VerifiedUserAccessor.GetVerifiedUser{TUser}"/>, so portal
    /// call sites don't repeat the <c>User</c> type argument. The verification itself lives in the
    /// framework pre-processor.
    /// </summary>
    public static User GetVerifiedUser(this HttpContext context) => context.GetVerifiedUser<User>();


    /// <summary>Kept as aliases of the framework's canonical <see cref="UserRoles"/> groups so the app's
    /// own base endpoints and the framework's authorize against the exact same role names.</summary>
    public static string[] GetUserOrHigherRoles() => UserRoles.UserOrHigher;

    public static string[] GetAdminOrHigherRoles() => UserRoles.AdminOrHigher;
}