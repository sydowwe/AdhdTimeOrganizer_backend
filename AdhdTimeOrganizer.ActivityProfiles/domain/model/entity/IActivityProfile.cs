namespace AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;

/// <summary>
/// The three 1:1 activity profiles — backlog, project and bucket list — as one family.
/// </summary>
/// <remarks>
/// <para>
/// They share no base class (each is a plain <c>BaseTableEntity</c>) but they share the shape that
/// matters for A9: a <b>unique</b> <c>ActivityId</c>, which makes them the only reference type in the
/// solution where repointing two rows onto one survivor is impossible rather than merely unusual. This
/// marker is what lets <c>ActivityProfileActivityReferenceSource</c> state that collision rule once
/// instead of three times.
/// </para>
/// <para>
/// Schema-neutral: it declares a property every one of them already has, so EF's model is unchanged and
/// no migration follows from it. It is deliberately <em>not</em> a base class — introducing one would
/// change the discovery order of these configurations and, through it, the derived FK constraint names
/// that this slice pins by hand.
/// </para>
/// </remarks>
public interface IActivityProfile
{
    long ActivityId { get; set; }
}
