namespace AdhdTimeOrganizer.Tracking.domain.model.@enum;

/// <summary>
/// A mapping's target shape — a filter-side convenience over the <c>…_TargetRequired</c> check
/// constraint, not a stored column.
/// </summary>
/// <remarks>
/// Shared by <b>both</b> mapping tables: the two grids' filters carry the same target group, so there is
/// one enum rather than a desktop copy and an android one. It was named
/// <c>TrackerDesktopMappingTypeEnum</c> until CQ-15 — correct by value on the android grid and
/// misleading by name, which is the sort of thing that gets "fixed" by adding a second copy.
/// </remarks>
public enum TrackerMappingTypeEnum
{
    Ignored,
    Activity,
    Category,
    Role,
    CategoryAndRole
}
