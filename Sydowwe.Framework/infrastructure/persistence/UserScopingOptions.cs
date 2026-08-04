namespace Sydowwe.Framework.infrastructure.persistence;

/// <summary>
/// Deployment switch for automatic per-user row scoping — the global EF query filter
/// <c>!IsAuthenticated || e.UserId == currentUserId</c> that
/// <see cref="UserDbContextExtensions.ApplyUserQueryFilters"/> puts on every <c>IEntityWithUser</c>.
///
/// <para><b>Default is off, deliberately.</b> This is only correct for a deployment where every row
/// belongs to exactly one user and nobody ever reads across users. In a deployment with an
/// admin/HR tier it is wrong in both directions at once, so it must be an explicit decision per
/// host rather than something a framework bump can switch on.</para>
///
/// <para><b>What "on" actually costs you.</b> Two asymmetries, neither of which this switch can fix
/// — they are inherent to a filter keyed on the ambient user:
/// <list type="bullet">
/// <item><b>Authenticated requests get narrower.</b> An admin reading another user's rows gets an
/// empty result, not a 403. Silent-empty is the worst failure mode for anything auditable, because
/// nothing is logged and the caller cannot tell "no rows" from "not allowed".</item>
/// <item><b>Background work gets wider.</b> Jobs run unauthenticated, so <c>!IsAuthenticated</c>
/// short-circuits to <c>true</c> and the filter matches everything. The gate is fully open exactly
/// where no user is watching.</item>
/// </list>
/// Anything that must see all rows regardless has to opt out explicitly with
/// <c>IgnoreQueryFilters()</c> — see <c>RefreshTokenService.Tokens</c>, which needs it so that
/// revoking another user's sessions does not silently update zero rows.</para>
///
/// <para><b>Bulk operations are affected too.</b> <c>ExecuteUpdateAsync</c> /
/// <c>ExecuteDeleteAsync</c> honour query filters, so retention purges and any other bulk path
/// silently change scope when this is enabled.</para>
///
/// <para><b>Restart-scoped, not hot-reloadable.</b> EF bakes query filters into the model and caches
/// the model per context type, so this is read once when the model is first built. Changing
/// appsettings requires a process restart. It deliberately does not use <c>IOptionsMonitor</c>:
/// a value that looks live but isn't is worse than one that is plainly startup-only.</para>
/// </summary>
public class UserScopingOptions
{
    public const string SectionName = "UserScoping";

    /// <summary>
    /// Whether the global per-user query filter is applied at all. <c>false</c> by default — see the
    /// class remarks for why this is not a safe default to flip.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// CLR type names (e.g. <c>"RefreshToken"</c>) of <c>IEntityWithUser</c> entities that must NOT
    /// be filtered, matched case-insensitively. Use for entities that are user-owned as data but are
    /// legitimately read and written across users by the application itself.
    /// <para>A compile-time equivalent exists — the <c>excludeTypes</c> argument on
    /// <see cref="UserDbContextExtensions.ApplyUserQueryFilters"/> — and is preferable when the
    /// exclusion is a property of the code rather than of the deployment: a typo here fails open
    /// (the entity stays filtered and reads go quiet), whereas a wrong <c>typeof</c> fails the
    /// build.</para>
    /// </summary>
    public List<string> ExcludedEntities { get; set; } = [];
}
