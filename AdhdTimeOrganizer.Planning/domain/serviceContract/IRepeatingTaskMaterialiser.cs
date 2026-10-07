using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;

namespace AdhdTimeOrganizer.Planning.domain.serviceContract;

/// <summary>
/// Puts auto-placing repeating rules (<see cref="RepeatingPlannerTask.AutoPlace"/>, FO6) onto the user's plan
/// for today and tomorrow, and keeps the occurrences it placed in step with later edits to the rule.
/// <para>
/// <b>None of these throw.</b> They run beside a read (<c>GET /calendar/day-plan/{date}</c>) or after a rule's
/// own write has committed, and neither of those should fail because the plan could not be filled in. A failure
/// is logged and the tracker cleared; the next read tries again, because nothing was decided.
/// </para>
/// <para>
/// <b>They commit.</b> Call them before staging writes of your own, or after your own save — never between.
/// </para>
/// </summary>
public interface IRepeatingTaskMaterialiser
{
    /// <summary>
    /// The day-plan read's hook: materialises today and tomorrow when <paramref name="readDate"/> is one of the
    /// two on the user's clock, and does nothing otherwise. Both days are decided together whichever was read,
    /// because the rule's watermark only moves forward — deciding tomorrow alone would skip today for good.
    /// </summary>
    Task PlaceForReadAsync(long userId, DateOnly readDate, CancellationToken ct = default);

    /// <summary>Materialises today and tomorrow for every due rule of the user.</summary>
    Task PlaceDueAsync(long userId, CancellationToken ct = default);

    /// <summary>
    /// After a rule edit has committed: brings the rule's <b>untouched</b> occurrences from today on up to date
    /// (or removes them, when the rule no longer places on that day), and places on any already-decided day the
    /// edit newly matches. <paramref name="before"/> is the rule as it was before the edit.
    /// </summary>
    Task ReconcileAfterEditAsync(RepeatingPlannerTask rule, RepeatingPlannerTask before, CancellationToken ct = default);
}
