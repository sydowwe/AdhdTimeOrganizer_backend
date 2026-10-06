using AdhdTimeOrganizer.Core.application.seam;

namespace AdhdTimeOrganizer.Core.application.@event;

/// <summary>
/// Raised after activity time has been committed to History, naming every activity it touched and how much
/// time that activity now has for the day. Two publishers: the desktop heartbeat (once per batch), and
/// History's own create paths — <c>POST /activity-history</c> and <c>POST /activity-history/session</c> — once
/// per (local day, activity) the new rows touch, so a timer or a hand-logged stretch ticks off the plan the same
/// way tracked time does (FO2).
/// </summary>
/// <remarks>
/// <para>
/// This is what stops Tracking writing into other slices. The heartbeat used to transition
/// <c>PlannerTask.Status</c> and tick off to-do / routine items inline, which made Tracking depend on
/// Planning, TodoLists and Routines — with writes, so none of it could be inverted the way History's
/// read-side filters were. Tracking now reports what it observed and stops there.
/// </para>
/// <para>
/// ⚠ <b>The completion automation is deliberately handled in one host-side handler, not one handler
/// per owning slice.</b> The rule it implements is exclusive — the to-do / routine fallback runs
/// <em>only</em> when no planner task matched — and independent subscribers cannot express that:
/// <c>Mode.WaitForAll</c> gives no ordering and no veto, so splitting it per slice would silently
/// double-complete an activity that has both a planner task and a to-do item. The host already
/// references every slice and already hosts the three completion fan-out handlers.
/// </para>
/// <para>
/// ⚠ <b>Every field here is on the user's own clock</b>, which is the clock a <c>Calendar.Date</c> and a
/// <c>PlannerTask.StartTime</c> are written on. A publisher must resolve the user's zone before building
/// this; deriving <see cref="Day"/> from a UTC instant is the bug the heartbeat shipped with, and every
/// publisher (History's creates, and Android sync when it comes) follows the same rule.
/// </para>
/// </remarks>
/// <param name="UserId">Owner of the tracked time. Handlers must scope on it explicitly.</param>
/// <param name="Day">The user's local date the batch's window falls on.</param>
/// <param name="WindowStart">The window's start, as the user's wall clock reads it.</param>
/// <param name="WindowEnd">
/// The window's end, as the user's wall clock reads it. Wraps to <c>00:00</c> for a window that closes at
/// midnight, which is the same midnight-wrap convention a planner task's own span uses.
/// </param>
/// <param name="Totals">
/// One entry per activity the batch attributed time to, carrying that activity's <em>whole-day</em>
/// total, not the batch delta — completion thresholds are cumulative.
/// </param>
public record ActivityTimeRecordedEvent(
    long UserId,
    DateOnly Day,
    TimeOnly WindowStart,
    TimeOnly WindowEnd,
    IReadOnlyList<ActivityDayTotal> Totals) : ISeamEvent;

/// <param name="ActivityId">The activity the time was recorded against.</param>
/// <param name="TotalSecondsOnDay">Seconds attributed to it across the whole of the user's <c>Day</c>.</param>
/// <param name="TodoListItemId">
/// The to-do item the time was recorded <em>for</em>, when the row carries that link (a timer or a log started
/// from the item). The to-do / routine fallback then completes that item rather than whichever open item shares
/// the activity. Null from the heartbeat, which only ever knows the activity.
/// </param>
/// <param name="RoutineTodoListId">Same as <paramref name="TodoListItemId"/>, for a routine item. Never both.</param>
public record ActivityDayTotal(
    long ActivityId,
    int TotalSecondsOnDay,
    long? TodoListItemId = null,
    long? RoutineTodoListId = null);
