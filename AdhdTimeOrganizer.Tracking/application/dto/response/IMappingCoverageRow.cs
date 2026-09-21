namespace AdhdTimeOrganizer.Tracking.application.dto.response.activityTracking;

/// <summary>
/// A distinct-entries grid row that carries the <c>coveredBy</c> chip. The SQL projection leaves it null;
/// <c>MappingCoverageGridExtensions</c> fills it in memory after the page (or the whole set, when the
/// <c>coverage</c> filter is on) is materialised.
/// </summary>
public interface IMappingCoverageRow
{
    MappingCoverageResponse? CoveredBy { get; set; }
}
