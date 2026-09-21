using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking.android;

namespace AdhdTimeOrganizer.Tracking.domain.helper.mapping;

/// <summary>
/// The android twin of <see cref="DesktopMappingRuleSet"/> over <c>(package name, app label)</c> — same
/// specificity-then-id order, same first-match-wins, active rules only.
/// </summary>
/// <remarks>
/// ⚠ <b>Nothing on the ingest side reads this yet.</b> <c>AndroidSyncEndpoint</c> stores sessions
/// without consulting the mapping table, so an android rule is created, listed, edited and deleted and
/// applied to nothing; its only readers are the distinct-entries grid's <c>coveredBy</c> chip (B24) and
/// the <c>canDelete</c> scan. The chip therefore means "an active rule exists that would match", with no
/// attribution behind it. This class exists so that when the sync does start resolving sessions it
/// resolves them <em>here</em>, and the grid and the ingest agree from the first heartbeat — the drift
/// the desktop side already had to be cured of (CQ-16).
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
