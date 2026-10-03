using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.History.domain.model.@enum;

namespace AdhdTimeOrganizer.History.domain.model.entity.activityHistory;

/// <summary>
/// One sitting with one timer — a Pomodoro (one <see cref="ActivityHistory"/> row per phase that ran) or a
/// countdown (exactly one row) — and how it ended.
/// </summary>
/// <remarks>
/// <para>
/// <b>It exists so that splitting a Pomodoro into its real phases does not multiply every count.</b> Before this,
/// a Pomodoro was written as one summed focus row and one summed break row, both dated at Start and overlapping
/// on the timeline. One row per phase fixes the time; grouping those rows here is what lets every surface that
/// counts "entries" count the sitting instead (see <c>HistorySittings</c>).
/// </para>
/// <para>
/// <b>The outcome and the focus-period counts describe what the timer did, not what the rows now say.</b> Editing
/// a row does not touch them, and they are never recomputed from the rows.
/// </para>
/// <para>
/// <b>Lifetime follows the rows.</b> Written all-or-nothing with its rows by
/// <c>CreateActivityHistorySessionEndpoint</c>; removed by <c>DeleteActivityHistoryEndpoint</c> when its last row
/// goes; cascaded with the account. Every read reaches a session only through a row, so a session with no rows
/// never appears anywhere. The one path that can leave one behind is a hard delete of the activity, which
/// cascades its rows in the database — the residue is an invisible row of four enum/int columns, gone with the
/// account.
/// </para>
/// </remarks>
public class ActivityHistorySession : BaseEntityWithUser
{
    public required TimerSessionKind Kind { get; set; }

    public required TimerSessionOutcome Outcome { get; set; }

    /// <summary>Focus periods the preset planned; null for a <see cref="TimerSessionKind.Timer"/>.</summary>
    public int? PlannedFocusPeriods { get; set; }

    /// <summary>
    /// Focus periods reached before the session ended — "stopped after 2 of 4". Never more than
    /// <see cref="PlannedFocusPeriods"/>; null for a <see cref="TimerSessionKind.Timer"/>.
    /// </summary>
    public int? CompletedFocusPeriods { get; set; }

    public ICollection<ActivityHistory> Records { get; set; } = [];
}
