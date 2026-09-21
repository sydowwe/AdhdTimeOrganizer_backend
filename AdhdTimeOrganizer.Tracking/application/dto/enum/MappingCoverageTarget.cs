namespace AdhdTimeOrganizer.Tracking.application.dto.@enum;

/// <summary>
/// The target group of the rule that covers a distinct-entries row — the three groups
/// <c>CK_Tracker*MappingByPattern_TargetRequired</c> names. Not <c>TrackerMappingTypeEnum</c>, which is
/// the mapping grid's five-way <em>filter</em> shape; the chip only needs to know what kind of thing the
/// rule points at.
/// </summary>
public enum MappingCoverageTarget
{
    Activity,
    RoleOrCategory,
    Ignored
}
