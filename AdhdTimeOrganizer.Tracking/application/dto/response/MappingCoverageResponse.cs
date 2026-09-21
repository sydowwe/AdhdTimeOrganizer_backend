using AdhdTimeOrganizer.Tracking.application.dto.@enum;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking;

namespace AdhdTimeOrganizer.Tracking.application.dto.response.activityTracking;

/// <summary>
/// The <c>coveredBy</c> chip on a distinct-entries row (B24): the mapping rule the ingest would pick for
/// that tuple today. Null on the row when no active rule matches.
/// </summary>
/// <param name="MappingId">The rule's id — the deep link into "edit this rule" on the Mappings tab.</param>
/// <param name="Target">Which of the three target groups the rule points at.</param>
/// <param name="TargetName">
/// The chip text: the activity's name, the role and/or category name (joined with <c>·</c> when both
/// are set), or null for an ignore rule. Sent here because the client has nothing else to render it from
/// without a second request per row.
/// </param>
public sealed record MappingCoverageResponse(long MappingId, MappingCoverageTarget Target, string? TargetName)
{
    private const string RoleCategorySeparator = " · ";

    /// <summary>
    /// Describes <paramref name="rule"/>, whose <c>Activity</c> / <c>Role</c> / <c>Category</c> navigations
    /// must already be loaded — the name is read off them, not queried.
    /// </summary>
    public static MappingCoverageResponse From(IPatternMappingRule rule)
    {
        if (rule.IsIgnored == true)
            return new MappingCoverageResponse(rule.Id, MappingCoverageTarget.Ignored, null);

        if (rule.ActivityId != null)
            return new MappingCoverageResponse(rule.Id, MappingCoverageTarget.Activity, rule.Activity?.Name);

        var names = new[] { rule.Role?.Name, rule.Category?.Name }.Where(n => !string.IsNullOrEmpty(n));
        var joined = string.Join(RoleCategorySeparator, names);

        return new MappingCoverageResponse(rule.Id, MappingCoverageTarget.RoleOrCategory, joined.Length == 0 ? null : joined);
    }
}
