namespace AdhdTimeOrganizer.History.domain.model.@enum;

/// <summary>
/// Which timer produced an <c>ActivityHistorySession</c>. A stopwatch never ends on its own and has no outcome,
/// so it is not a session and has no member here — it keeps writing plain rows.
/// </summary>
public enum TimerSessionKind
{
    /// <summary>A focus/break cycle; one row per phase that actually ran.</summary>
    Pomodoro,

    /// <summary>A plain countdown; always exactly one row.</summary>
    Timer
}

/// <summary>
/// How the sitting ended, as the client's timer store observed it. <b>Stored as told, never derived from the
/// rows</b> — a focus period cut short by Stop and one that ran its full length look identical once rounded to
/// whole minutes, so the rows cannot say which it was.
/// </summary>
public enum TimerSessionOutcome
{
    /// <summary>The plan's final boundary was reached — for a countdown, including one that ran out while the app was closed.</summary>
    Completed,

    /// <summary>Stop was pressed before the plan was done.</summary>
    StoppedEarly,

    /// <summary>
    /// Pomodoro only: the app was closed across a boundary that was not the last, and the session ended there
    /// rather than replaying the cycle.
    /// </summary>
    RanOutWhileAway
}

/// <summary>
/// Which part of a Pomodoro a row is. It cannot be read off the activity, because the rest activity may be the
/// same activity as the focus one. Null on a <see cref="TimerSessionKind.Timer"/> row and on every row outside a
/// session.
/// </summary>
/// <remarks>
/// Deliberately not <see cref="TimerBoundaryPhase"/>: that one describes the boundary a push announces rather than
/// the stretch a row records, and carries a <c>Plain</c> member no row may have.
/// </remarks>
public enum TimerSessionPhase
{
    Focus,
    ShortBreak,
    LongBreak
}
