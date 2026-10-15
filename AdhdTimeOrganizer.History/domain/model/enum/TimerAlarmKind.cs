namespace AdhdTimeOrganizer.History.domain.model.@enum;

/// <summary>
/// Which notification a <c>TimerBoundaryAlarm</c> row raises when it fires. Two kinds, two payloads, one table —
/// all of them this slice's (<c>application/notification</c>).
/// Appended, never reordered — the column stores the ordinal, and every row written before this existed is 0.
/// </summary>
public enum TimerAlarmKind
{
    /// <summary>A countdown or pomodoro phase ends: <c>HistoryNotificationTypes.TimerBoundary</c>, quiet-hours exempt.</summary>
    Boundary,

    /// <summary>
    /// A running stopwatch checks in: <c>HistoryNotificationTypes.StopwatchCheckIn</c>. Carries <c>ElapsedMinutes</c> and no
    /// phase. Skipped at fire time when the user is inside their quiet hours.
    /// </summary>
    StopwatchCheckIn
}
