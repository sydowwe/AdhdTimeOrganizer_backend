namespace AdhdTimeOrganizer.History.domain.model.@enum;

/// <summary>
/// Which notification a <c>TimerBoundaryAlarm</c> row raises when it fires. Owned here, not in the contracts: the
/// framework sees two notification kinds and two payloads, and only this slice knows they share one table.
/// Appended, never reordered — the column stores the ordinal, and every row written before this existed is 0.
/// </summary>
public enum TimerAlarmKind
{
    /// <summary>A countdown or pomodoro phase ends: <c>NotificationType.TimerBoundary</c>, quiet-hours exempt.</summary>
    Boundary,

    /// <summary>
    /// A running stopwatch checks in: <c>NotificationType.StopwatchCheckIn</c>. Carries <c>ElapsedMinutes</c> and no
    /// phase. Skipped at fire time when the user is inside their quiet hours.
    /// </summary>
    StopwatchCheckIn
}
