using AdhdTimeOrganizer.Tracking.application.dto.response.activityTracking;
using Sydowwe.Framework.application.dto.response.@base;

namespace AdhdTimeOrganizer.Tracking.application.dto.response.activityTracking.android;

public record AndroidDistinctEntriesResponse : IIdResponse, IMappingCoverageRow
{
    public long Id { get; init; } = 0;
    public required string PackageName { get; set; }
    public string? AppLabel { get; set; }

    /// <summary>
    /// The active android rule that would match this pair, or null when none does (B24). ⚠ On this source
    /// it means "a rule exists that would match" and nothing more — <c>AndroidSyncEndpoint</c> does not
    /// apply the rules yet (see <c>AndroidMappingRuleSet</c>). Computed after the query — not a sort key.
    /// </summary>
    public MappingCoverageResponse? CoveredBy { get; set; }
}
