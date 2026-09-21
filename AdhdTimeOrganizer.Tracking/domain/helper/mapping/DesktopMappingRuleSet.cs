using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking.desktop;

namespace AdhdTimeOrganizer.Tracking.domain.helper.mapping;

/// <summary>
/// A user's active desktop rules, in the order the ingest walks them, and the one answer to "which rule
/// covers this <c>(process, product, window title)</c> tuple?" — <b>first match wins</b>.
/// </summary>
/// <remarks>
/// <para>
/// This is the matcher <c>DesktopActivityHeartbeatEndpoint</c> attributes seconds with, and the same
/// instance type the distinct-entries grid resolves its <c>coveredBy</c> chip with (B24). The two must
/// not drift: the grid's whole purpose is to tell the user what the ingest <em>would</em> do with a
/// tuple today, so it has to share the code path rather than approximate it. Add a pattern column here
/// and both sides see it; reorder here and both sides reorder.
/// </para>
/// <para>
/// <b>Specificity first, then id (CQ-16).</b> Two rules can match one entry — "any window titled
/// *GitHub*" and "chrome.exe with a window titled *GitHub*" are both legitimate, and the second is what
/// the user meant when they wrote it. Nothing ordered the list before, so which of them attributed the
/// seconds depended on the plan for an unordered read; it was stable in practice and stable for the
/// wrong reason, and an insert or a vacuum could change it without anything changing in the user's
/// rules. Specificity is the number of pattern columns a rule constrains, since a rule naming two
/// columns matches a strict subset of what its one-column prefix matches; then the id, so the older of
/// two equally specific rules wins. Ordering does not resolve genuinely overlapping rules of the same
/// shape (two wildcards on the same column); it makes the winner stable and explicable, which is what
/// the user needs to be able to reason about their own list.
/// </para>
/// <para>
/// <b>Active rules only.</b> <see cref="ActiveFor"/> is the query both callers start from; a rule with
/// <c>IsActive = false</c> is not in the set and therefore neither attributes nor covers. Build one set
/// per request — the matcher's regex cache is per instance.
/// </para>
/// </remarks>
public sealed class DesktopMappingRuleSet
{
    private readonly List<TrackerDesktopMappingByPattern> ordered;
    private readonly PatternMatcher matcher = new();

    public DesktopMappingRuleSet(IEnumerable<TrackerDesktopMappingByPattern> activeRules)
    {
        ordered = OrderBySpecificity(activeRules);
    }

    /// <summary>The rules the ingest applies for <paramref name="userId"/>: the user's own, active ones.</summary>
    public static IQueryable<TrackerDesktopMappingByPattern> ActiveFor(DbContext dbContext, long userId) =>
        dbContext.Set<TrackerDesktopMappingByPattern>()
            .Where(m => m.UserId == userId && m.IsActive);

    /// <summary>The rule the ingest would pick for this tuple today, or null when no active rule matches.</summary>
    public TrackerDesktopMappingByPattern? Resolve(string? processName, string? productName, string? windowTitle)
    {
        foreach (var rule in ordered)
            if (Matches(rule, processName, productName, windowTitle))
                return rule;

        return null;
    }

    private bool Matches(TrackerDesktopMappingByPattern mapping, string? processName, string? productName, string? windowTitle)
    {
        if (mapping.ProcessName != null && mapping.ProcessNameMatchType != null)
            if (!matcher.Matches(processName, mapping.ProcessName, mapping.ProcessNameMatchType.Value))
                return false;

        if (mapping.ProductName != null && mapping.ProductNameMatchType != null)
            if (!matcher.Matches(productName, mapping.ProductName, mapping.ProductNameMatchType.Value))
                return false;

        if (mapping.WindowTitle != null && mapping.WindowTitleMatchType != null)
            if (!matcher.Matches(windowTitle, mapping.WindowTitle, mapping.WindowTitleMatchType.Value))
                return false;

        return true;
    }

    private static List<TrackerDesktopMappingByPattern> OrderBySpecificity(IEnumerable<TrackerDesktopMappingByPattern> mappings) =>
        mappings
            .OrderByDescending(m =>
                (m.ProcessName != null ? 1 : 0) +
                (m.ProductName != null ? 1 : 0) +
                (m.WindowTitle != null ? 1 : 0))
            .ThenBy(m => m.Id)
            .ToList();
}
