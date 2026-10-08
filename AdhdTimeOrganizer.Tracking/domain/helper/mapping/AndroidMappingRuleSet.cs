using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking.android;

namespace AdhdTimeOrganizer.Tracking.domain.helper.mapping;

/// <summary>
/// The android twin of <see cref="DesktopMappingRuleSet"/> over <c>(package name, app label)</c> — same
/// specificity-then-id order, same first-match-wins, active rules only.
/// </summary>
/// <remarks>
/// <c>AndroidSyncEndpoint</c> resolves every newly stored session through this (FO9), and the distinct-entries
/// grid computes its <c>coveredBy</c> chip with it (B24) — one matcher for both, so the chip says what the sync
/// does with a session today: the drift the desktop side already had to be cured of (CQ-16). Before FO9 the
/// sync applied no rule at all and the chip had no attribution behind it.
/// </remarks>
public sealed class AndroidMappingRuleSet
{
    private readonly List<TrackerAndroidMappingByPattern> ordered;
    private readonly PatternMatcher matcher = new();

    public AndroidMappingRuleSet(IEnumerable<TrackerAndroidMappingByPattern> activeRules)
    {
        ordered = OrderBySpecificity(activeRules);
    }

    /// <summary>The user's own, active rules — the set an ingest would apply.</summary>
    public static IQueryable<TrackerAndroidMappingByPattern> ActiveFor(DbContext dbContext, long userId) =>
        dbContext.Set<TrackerAndroidMappingByPattern>()
            .Where(m => m.UserId == userId && m.IsActive);

    /// <summary>The rule that would be picked for this pair, or null when no active rule matches.</summary>
    public TrackerAndroidMappingByPattern? Resolve(string? packageName, string? appLabel)
    {
        foreach (var rule in ordered)
            if (Matches(rule, packageName, appLabel))
                return rule;

        return null;
    }

    private bool Matches(TrackerAndroidMappingByPattern mapping, string? packageName, string? appLabel)
    {
        if (mapping.PackageName != null && mapping.PackageNameMatchType != null)
            if (!matcher.Matches(packageName, mapping.PackageName, mapping.PackageNameMatchType.Value))
                return false;

        if (mapping.AppLabel != null && mapping.AppLabelMatchType != null)
            if (!matcher.Matches(appLabel, mapping.AppLabel, mapping.AppLabelMatchType.Value))
                return false;

        return true;
    }

    private static List<TrackerAndroidMappingByPattern> OrderBySpecificity(IEnumerable<TrackerAndroidMappingByPattern> mappings) =>
        mappings
            .OrderByDescending(m =>
                (m.PackageName != null ? 1 : 0) +
                (m.AppLabel != null ? 1 : 0))
            .ThenBy(m => m.Id)
            .ToList();
}
