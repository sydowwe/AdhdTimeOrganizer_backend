using AdhdTimeOrganizer.Core.domain.model.entity.user;
using Sydowwe.Framework.Contracts.notification.payload;

namespace AdhdTimeOrganizer.History.domain.model.entity.timerAlarm;

/// <summary>
/// One pending end-of-phase alarm for a running timer: "at this instant, push a notification to this user".
/// <para>
/// <b>This is not timer state and must never become it.</b> The countdown stays client-side and the server is
/// never authoritative for when the timer ends — the row exists only so the <i>notification</i> survives the
/// tab that scheduled it. A <c>setTimeout</c> cannot fire in a closed tab, so closing the laptop lid on a
/// 25-minute focus phase used to mean nothing rang; a row here plus a one-shot Scheduler trigger is the whole
/// fix. Nothing reads these rows to reconstruct a session.
/// </para>
/// <para>
/// <b>The table self-drains.</b> A row is deleted when its alarm fires, when the session is cancelled, and
/// when the boot registrar finds it stale — so there is no retention job and no <c>FiredAt</c> flag. Rows live
/// for the length of one timer, typically minutes. That is also why <see cref="ActivityId"/> is a plain id
/// with no FK and no navigation: an activity deleted mid-session leaves an orphan that resolves to a
/// name-less body and is gone within the hour, which is not worth a constraint in the
/// <c>Activity</c> FK family.
/// </para>
/// </summary>
public class TimerBoundaryAlarm : BaseEntityWithUser
{
    /// <summary>
    /// Groups every boundary of one timer session, so Stop cancels the whole cycle in one call. Client-issued
    /// (the session has no server-side identity — nothing about a running timer is persisted until it is
    /// logged as history), which is what lets a reloaded tab cancel a set it did not itself register.
    /// </summary>
    public required Guid SessionId { get; set; }

    /// <summary>
    /// The absolute UTC instant the alarm fires at.
    /// <para>
    /// <b>Scheduled on this instant, never re-derived from a duration against the server clock.</b> The
    /// boundary was computed from the client's own clock, and re-deriving it here would put the two clocks'
    /// skew straight into the alarm.
    /// </para>
    /// </summary>
    public required DateTime BoundaryAt { get; set; }

    /// <summary>Which boundary this is — picks the notification's title.</summary>
    public required TimerBoundaryPhase Phase { get; set; }

    /// <summary>
    /// The activity the timer was started for, or null when it named none. Resolved to a display name at fire
    /// time rather than snapshotted, so a renamed activity reads correctly. See the type remarks for why this
    /// carries no FK.
    /// </summary>
    public long? ActivityId { get; set; }

    /// <summary>1-based position of this period within its cycle; null for a plain countdown.</summary>
    public int? PhaseIndex { get; set; }

    public int? PhaseTotal { get; set; }

    /// <summary>1-based position of the cycle within the preset; null for a plain countdown.</summary>
    public int? CycleIndex { get; set; }

    public int? CycleTotal { get; set; }

    /// <summary>
    /// Relative app route the service worker opens when the push is clicked. Client-supplied because the route
    /// belongs to the SPA, validated to be relative so a push cannot be turned into an off-site redirect.
    /// </summary>
    public string? Url { get; set; }

    /// <summary>
    /// The Scheduler job key for this alarm's one-shot trigger. Derived from the row id, so it is stable
    /// across a restart and the boot registrar re-registers the same key rather than a duplicate.
    /// </summary>
    public string JobKey => JobKeyFor(Id);

    /// <inheritdoc cref="JobKey"/>
    public static string JobKeyFor(long alarmId) => $"history.timer-boundary.{alarmId}";

    /// <summary>
    /// The browser notification tag, derived from the boundary instant.
    /// <para>
    /// <b>Load-bearing, not decoration.</b> When the tab is still open the page shows its own notification
    /// under this tag and the push replaces it in place instead of stacking a second one — that is how the
    /// double alarm is avoided. Deriving it from the instant (rather than the row id) is what lets the client
    /// rebuild the identical tag with no round trip, which is the only reason the suppression works.
    /// </para>
    /// </summary>
    public string Tag => TagFor(BoundaryAt);

    /// <inheritdoc cref="Tag"/>
    public static string TagFor(DateTime boundaryAt) =>
        $"timer-boundary-{new DateTimeOffset(DateTime.SpecifyKind(boundaryAt, DateTimeKind.Utc)).ToUnixTimeMilliseconds()}";
}
