namespace Sydowwe.Framework.domain.auth;

/// <summary>
/// Ready-made <see cref="IRoleCatalog"/> built from a list of <see cref="RoleDefinition"/>. Hosts
/// normally construct one of these rather than implementing the interface.
/// </summary>
public class RoleCatalog : IRoleCatalog
{
    private readonly Dictionary<RoleTier, string[]> _atOrAbove;

    public RoleCatalog(IEnumerable<RoleDefinition> roles)
    {
        All = roles.OrderBy(r => r.Tier).ToArray();

        var duplicate = All.GroupBy(r => r.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException($"Duplicate role name '{duplicate.Key}' in the role catalog.");

        // Precomputed: AtOrAbove is called once per endpoint during startup configuration, and the
        // answer can never change afterwards.
        _atOrAbove = Enum.GetValues<RoleTier>()
            .ToDictionary(tier => tier, tier => All.Where(r => r.Tier >= tier).Select(r => r.Name).ToArray());

        var defaults = All.Where(r => r.IsDefault).ToArray();
        if (defaults.Length > 1)
            throw new InvalidOperationException(
                $"Role catalog declares {defaults.Length} default roles ({string.Join(", ", defaults.Select(r => r.Name))}); exactly one is allowed.");

        _defaultRoleName = defaults.SingleOrDefault()?.Name;
    }

    private readonly string? _defaultRoleName;

    public IReadOnlyList<RoleDefinition> All { get; }

    public string[] AtOrAbove(RoleTier tier) => _atOrAbove.TryGetValue(tier, out var names) ? names : [];

    public string DefaultRoleName => _defaultRoleName
                                     ?? throw new InvalidOperationException(
                                         "The role catalog declares no default role (RoleDefinition.IsDefault). " +
                                         "Registration cannot assign a role without one.");
}

/// <summary>
/// Process-wide access to the deployment's <see cref="IRoleCatalog"/>.
///
/// <para><b>Why static rather than DI.</b> Endpoint role gates are read inside FastEndpoints'
/// <c>Configure()</c>, which runs during endpoint registration — before any scope exists and, for
/// most endpoints, before the service provider is usable. The values are also immutable for the
/// lifetime of the process, so there is nothing a container would buy here. Call
/// <see cref="Configure"/> exactly once, at startup, before <c>AddFastEndpoints()</c>.</para>
///
/// <para>Reading before configuration throws rather than returning an empty array. An empty array
/// would mean "no roles allowed", which FastEndpoints would happily register — turning a wiring
/// mistake into endpoints that deny everyone at runtime, long after the cause is visible. Failing at
/// startup puts the error where someone can act on it.</para>
/// </summary>
public static class FrameworkRoles
{
    private static IRoleCatalog? _catalog;
    private static RoleTier _defaultEndpointTier = RoleTier.Admin;

    /// <summary>
    /// Installs the deployment's catalog and, optionally, the tier the framework's base endpoints gate
    /// on when they don't override <c>AllowedRoles()</c>. Call once, at startup, before endpoint
    /// registration.
    /// </summary>
    /// <param name="defaultEndpointTier">
    /// Omit it and the default stays <see cref="RoleTier.Admin"/> — deny by default. Pass
    /// <see cref="RoleTier.User"/> only in a deployment where every row is owned by exactly one user
    /// AND the base reads scope rows to the caller; see <see cref="DefaultEndpointTier"/>.
    /// </param>
    public static void Configure(IRoleCatalog catalog, RoleTier? defaultEndpointTier = null)
    {
        _catalog = catalog;
        if (defaultEndpointTier is { } tier)
            _defaultEndpointTier = tier;
    }

    /// <summary>
    /// The tier every base endpoint falls back to. <see cref="RoleTier.Admin"/> unless the host says
    /// otherwise.
    ///
    /// <para><b>Why this is a host decision and not a constant.</b> "Who can reach an endpoint nobody
    /// explicitly gated" is the single highest-stakes default in the framework, and the right answer
    /// differs per product: an app whose users are all plain <c>User</c> needs a User default or its
    /// endpoints are unreachable, while an HR/payroll deployment needs Admin or every un-overridden
    /// grid becomes readable by any authenticated employee. Baking either one into the shared base
    /// classes makes the other product's requirement arrive as a silent one-line diff — which is
    /// exactly how this default got flipped once already.</para>
    ///
    /// <para><b>Lowering it is not free.</b> The base reads
    /// (<c>Grid</c>/<c>Filter</c>/<c>Sort</c>/<c>FilterSort</c>) do no row scoping by default —
    /// <c>ApplyUserScoping</c> is a no-op — so a <see cref="RoleTier.User"/> default means "any
    /// authenticated caller can page the whole table" unless each endpoint overrides it.</para>
    /// </summary>
    public static RoleTier DefaultEndpointTier => _defaultEndpointTier;

    public static IRoleCatalog Catalog => _catalog
                                          ?? throw new InvalidOperationException(
                                              "No IRoleCatalog has been configured. Call FrameworkRoles.Configure(...) during startup, " +
                                              "before AddFastEndpoints(), so endpoint role gates can be resolved.");

    /// <summary>Shorthand for <c>Catalog.AtOrAbove(tier)</c>.</summary>
    public static string[] AtOrAbove(RoleTier tier) => Catalog.AtOrAbove(tier);

    /// <summary>
    /// Test/host seam: clears the configured catalog and restores the deny-by-default endpoint tier.
    /// Only needed by test hosts that build several applications in one process with different
    /// catalogs — resetting the tier too keeps one host's widened default from leaking into the next.
    /// </summary>
    public static void Reset()
    {
        _catalog = null;
        _defaultEndpointTier = RoleTier.Admin;
    }
}