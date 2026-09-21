namespace AdhdTimeOrganizer.Tracking.application.dto.@enum;

/// <summary>
/// The <c>coverage</c> filter on the two distinct-entries grids (B24): which tuples to list, judged by
/// whether an <em>active</em> mapping rule would match them today.
/// </summary>
/// <remarks>
/// <para>
/// Nullable on both filter records and null means <see cref="All"/> — the grid stays the raw observation
/// record by default. <see cref="Uncovered"/> is the "still to decide" view; <see cref="Covered"/> is the
/// audit view ("which rows does a rule actually catch?").
/// </para>
/// <para>
/// Wire format is the member name (<c>JsonStringEnumConverter</c>, no naming policy); an undefined value
/// is a 400 from the grid validators' <c>IsInEnum</c>. Patterns have no SQL translation, so anything but
/// <see cref="All"/> is resolved in memory over the whole distinct set and <c>itemsCount</c> is exact —
/// see <c>MappingCoverageGridExtensions</c>.
/// </para>
/// </remarks>
public enum MappingCoverage
{
    All,
    Uncovered,
    Covered
}
