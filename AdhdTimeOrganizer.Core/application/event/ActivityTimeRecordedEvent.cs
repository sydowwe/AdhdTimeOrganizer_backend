using FastEndpoints;

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
/// </remarks>
/// <param name="UserId">Owner of the tracked time. Handlers must scope on it explicitly.</param>
/// <param name="Day">The day the batch's window falls on, in the window's own terms.</param>
/// <param name="Totals">
/// One entry per activity the batch attributed time to, carrying that activity's <em>whole-day</em>
/// total, not the batch delta — completion thresholds are cumulative.
/// </param>
public record ActivityTimeRecordedEvent(long UserId, DateOnly Day, IReadOnlyList<ActivityDayTotal> Totals) : IEvent;

/// <param name="ActivityId">The activity a tracker mapping attributed the time to.</param>
/// <param name="TotalSecondsOnDay">Seconds attributed to it across the whole of <c>Day</c>.</param>
public record ActivityDayTotal(long ActivityId, int TotalSecondsOnDay);
