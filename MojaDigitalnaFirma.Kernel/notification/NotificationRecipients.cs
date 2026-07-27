using Sydowwe.Framework.domain.@enum;

namespace MojaDigitalnaFirma.Kernel.notification;

/// <summary>
/// Describes who a notification targets. Resolved to concrete user ids by the dispatcher
/// (Core.Notifications). Construct via the factory methods rather than the initializer.
/// </summary>
public sealed class NotificationRecipients
{
    public IReadOnlyCollection<long>? UserIds { get; private init; }
    public IReadOnlyCollection<UserRoleEnum>? Roles { get; private init; }
    public bool AllUsers { get; private init; }

    private NotificationRecipients()
    {
    }

    /// <summary>Target a single user.</summary>
    public static NotificationRecipients User(long userId) => new() { UserIds = [userId] };

    /// <summary>Target an explicit set of users.</summary>
    public static NotificationRecipients Users(IEnumerable<long> userIds) => new() { UserIds = userIds.Distinct().ToArray() };

    /// <summary>Target every user holding the given role.</summary>
    public static NotificationRecipients InRole(UserRoleEnum role) => new() { Roles = [role] };

    /// <summary>
    /// Target every user holding any of the given roles (deduplicated). Use for cumulative gates like
    /// "admin or higher" (Admin + Root) — a user is notified once even if they hold several.
    /// </summary>
    public static NotificationRecipients InRoles(params UserRoleEnum[] roles) => new() { Roles = roles.Distinct().ToArray() };

    /// <summary>Target every Admin (does not include Root-only — use InRole for that).</summary>
    public static NotificationRecipients Admins() => new() { Roles = [UserRoleEnum.Admin] };

    /// <summary>Target all users.</summary>
    public static NotificationRecipients Everyone() => new() { AllUsers = true };
}