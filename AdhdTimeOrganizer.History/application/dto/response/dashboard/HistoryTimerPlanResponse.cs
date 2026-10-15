namespace AdhdTimeOrganizer.History.application.dto.response.activityHistory.dashboard;

/// <summary>
/// How the range's timers compared with the focus they got: the focus the timers were set for, the focus the rows
/// hold, and how the sessions ended. Aggregates only.
/// </summary>
/// <remarks>
/// <para>
/// The three outcome counts split <see cref="SessionCount"/> exactly. The two focus sums are taken over the
/// <see cref="CompletedCount"/> + <see cref="StoppedEarlyCount"/> sessions, the same sessions on both sides, so the
/// sums can be compared. A <c>RanOutWhileAway</c> session is counted but kept out of both sums: its timer ran with
/// nobody there, and summing it would make an abandoned Pomodoro read as a plan met.
/// </para>
/// <para>
/// Deliberately carries no share. A client that wants one can divide, but "77%" reads as a grade of the person.
/// </para>
/// </remarks>
public record HistoryTimerPlanResponse
{
    /// <summary>Sessions in the range whose plan was recorded. Includes <see cref="RanOutWhileAwayCount"/>.</summary>
    public required int SessionCount { get; init; }

    /// <summary>
    /// Sessions in the range with no recorded plan (written before OP6, 2026-10-03). Counted in nothing else, so the
    /// client can name them rather than silently count less.
    /// </summary>
    public required int WithoutPlanCount { get; init; }

    /// <summary>Σ <c>PlannedLength</c> over the completed and stopped-early sessions with a plan.</summary>
    public required long PlannedFocusSeconds { get; init; }

    /// <summary>Σ the focus rows (phase null or Focus) of the same sessions, as the rows are now.</summary>
    public required long ActualFocusSeconds { get; init; }

    public required int CompletedCount { get; init; }

    public required int StoppedEarlyCount { get; init; }

    public required int RanOutWhileAwayCount { get; init; }

    /// <summary>
    /// Σ (planned − focus) over <see cref="StoppedEarlyCount"/> sessions only, each clamped at zero. A completed
    /// countdown can sit a minute under its plan because rows are whole minutes; that is rounding, not a shortfall.
    /// </summary>
    public required long StoppedEarlyShortfallSeconds { get; init; }
}
