using AdhdTimeOrganizer.History.domain.model.entity.timerAlarm;
using Sydowwe.Framework.Contracts.notification.payload;

namespace AdhdTimeOrganizer.History.application.dto.request.timerAlarm;

/// <summary>
/// The whole alarm set for one timer session, posted once when the user presses Start.
/// <para>
/// <b>Batched, not one call per boundary.</b> A pomodoro schedules its entire cycle up front — a 4-period,
/// 2-cycle preset is 15 boundaries — because rescheduling at each phase transition would need the live tab
/// whose absence is the entire bug this feature fixes.
/// </para>
/// </summary>
public record ScheduleTimerAlarmsRequest
{
    /// <summary>
    /// Client-issued identity for the session, and the handle every later cancel uses. A running timer has no
    /// server-side id — nothing about it is persisted until it is logged as history — so the client owns this,
    /// and holding it in the session's own stored state is what lets a reloaded tab cancel a set it did not
    /// register itself.
    /// </summary>
    public required Guid SessionId { get; init; }

    /// <summary>The activity the timer is running for, or null when the user named none.</summary>
    public long? ActivityId { get; init; }

    /// <summary>
    /// Relative SPA route the push opens on click (e.g. <c>/activity-history/pomodoro</c>). Validated to be
    /// relative — an absolute URL here would let a push become an off-site redirect.
    /// </summary>
    public string? Url { get; init; }

    public required List<TimerBoundaryRequest> Boundaries { get; init; }

    /// <summary>Maps the posted set onto entities. <c>UserId</c> and <c>SessionId</c> are stamped by the service.</summary>
    public List<TimerBoundaryAlarm> ToEntities() =>
        Boundaries.Select(b => new TimerBoundaryAlarm
        {
            SessionId = SessionId,
            BoundaryAt = b.BoundaryAt,
            Phase = b.Phase,
            ActivityId = ActivityId,
            PhaseIndex = b.PhaseIndex,
            PhaseTotal = b.PhaseTotal,
            CycleIndex = b.CycleIndex,
            CycleTotal = b.CycleTotal,
            Url = Url
        }).ToList();
}

/// <summary>One phase boundary of the session.</summary>
public record TimerBoundaryRequest
{
    /// <summary>
    /// The absolute UTC instant this boundary lands on.
    /// <para>
    /// <b>An instant, not a duration.</b> It was computed from the client's own <c>Date.now()</c>, and the
    /// server schedules it verbatim — re-deriving it from a duration against the server clock would put the
    /// two clocks' skew straight into the alarm.
    /// </para>
    /// </summary>
    public required DateTime BoundaryAt { get; init; }

    public required TimerBoundaryPhase Phase { get; init; }

    /// <summary>1-based position of this period within its cycle; null for a plain countdown.</summary>
    public int? PhaseIndex { get; init; }

    public int? PhaseTotal { get; init; }

    /// <summary>1-based position of the cycle within the preset; null for a plain countdown.</summary>
    public int? CycleIndex { get; init; }

    public int? CycleTotal { get; init; }
}

/// <summary>Cancels every alarm registered under one session. A no-op when the session is unknown.</summary>
public record CancelTimerAlarmsRequest
{
    public required Guid SessionId { get; init; }
}
