using AdhdTimeOrganizer.Core.application.seam;

namespace AdhdTimeOrganizer.Core.application.@event;

/// <summary>
/// Raised once per heartbeat batch, after the tracked time has been persisted and attributed, naming
/// every activity the batch touched and how much time that activity now has for the day.
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
/// this; deriving <see cref="Day"/> from a UTC instant is the bug the heartbeat shipped with, and any
/// future publisher (Android sync, timers) has to follow the same rule.
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

/// <param name="ActivityId">The activity a tracker mapping attributed the time to.</param>
/// <param name="TotalSecondsOnDay">Seconds attributed to it across the whole of the user's <c>Day</c>.</param>
public record ActivityDayTotal(long ActivityId, int TotalSecondsOnDay);
