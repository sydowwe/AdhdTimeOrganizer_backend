using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using Sydowwe.Framework.domain.valueObject;

namespace AdhdTimeOrganizer.History.domain.model.entity.activityHistory;

public class ActivityHistory : BaseEntityWithActivity
{
    public required DateTime StartTimestamp { get; set; }
    public required IntTime Length { get; set; }

    public DateTime EndTimestamp { get; set; }

    /// <summary>
    /// The to-do item this stretch of time was recorded for, or null when it was not recorded from a
    /// to-do item — which is most rows: everything the desktop / web-extension heartbeat attributes
    /// arrives keyed by activity alone.
    /// </summary>
    /// <remarks>
    /// Stamped when the user accepts the save-to-history prompt raised on completing an item, and on
    /// completing a <c>TodoListStep</c>, where it carries the <b>parent item's</b> id — the recap is
    /// item-level, so step granularity would buy nothing and cost a third nullable FK.
    /// <para>
    /// Navigation-free on purpose, and configured host-side in
    /// <c>AppDbContext.ConfigureCrossSliceRelationships</c>: this slice cannot see
    /// AdhdTimeOrganizer.TodoLists, and giving it a navigation would invert the direction the split
    /// depends on. This is a link, not a copy — <c>ActivityHistory</c> remains the sole owner of
    /// logged durations.
    /// </para>
    /// </remarks>
    public long? TodoListItemId { get; set; }

    /// <summary>
    /// The routine to-do item this stretch of time was recorded for, or null. The
    /// <see cref="TodoListItemId"/> remarks apply verbatim; the two are mutually exclusive, since a
    /// recording is prompted by completing one item or the other — enforced by
    /// <c>ActivityHistoryValidator</c>.
    /// </summary>
    public long? RoutineTodoListId { get; set; }
}