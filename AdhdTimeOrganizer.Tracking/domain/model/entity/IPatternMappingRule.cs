using AdhdTimeOrganizer.Core.domain.model.entity.activity;

namespace AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking;

/// <summary>
/// The target half a desktop and an android mapping rule have in common — everything but the pattern
/// columns. Lets the coverage chip on the two distinct-entries grids be built from either table by one
/// piece of code.
/// </summary>
/// <remarks>
/// The three target groups are the ones <c>CK_Tracker*MappingByPattern_TargetRequired</c> names: ignored,
/// activity, or role/category (either or both). <see cref="IsActive"/> is here because an inactive rule
/// matches nothing at ingest and therefore covers nothing.
/// </remarks>
public interface IPatternMappingRule
{
    long Id { get; }
    bool IsActive { get; }
    bool? IsIgnored { get; }
    long? ActivityId { get; }
    Activity? Activity { get; }
    ActivityRole? Role { get; }
    ActivityCategory? Category { get; }
}
