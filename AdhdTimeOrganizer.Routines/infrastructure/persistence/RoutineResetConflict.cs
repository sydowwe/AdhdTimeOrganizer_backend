using Npgsql;

namespace AdhdTimeOrganizer.Routines.infrastructure.persistence;

/// <summary>
/// Recognises the collision that happens when two callers close the same routine cycle at once.
/// <para>
/// The lazy reset runs from three places — the grouped read, the toggle and the nightly sweep — and none of
/// them locks. Two browser tabs in the same second, or a read landing while the 02:00 sweep is mid-flight,
/// both compute the same elapsed boundary and both try to write it. The loser surfaces as one of exactly two
/// exceptions: a unique violation on <c>routine_period_completion (time_period_id, period_start)</c> if the
/// completion insert is what the server executed first, or a <see cref="DbUpdateConcurrencyException"/> on the
/// period's <c>row_version</c> if the period update was. Which one you get is EF's batch ordering, so a caller
/// that handles only one of them still 500s half the time.
/// </para>
/// <para>
/// Swallowing is the correct response, and only because the reset is idempotent by construction: every value
/// it writes is derived from the period and the clock alone, so the winner wrote exactly what the loser was
/// about to. What a caller must still do is decide what happens to its own uncommitted work — see the three
/// call sites; none of them can simply ignore the exception and carry on saving.
/// </para>
/// </summary>
public static class RoutineResetConflict
{
    /// <summary>
    /// True when <paramref name="ex"/> means "someone else already closed this cycle".
    /// <para>
    /// Only ever wrap a save whose entire change set is the reset. A unique violation from an unrelated insert
    /// in the same batch looks identical from here and would be swallowed with it.
    /// </para>
    /// </summary>
    public static bool IsLostRace(Exception ex) =>
        ex is DbUpdateConcurrencyException
        || (ex is DbUpdateException dbEx
            && dbEx.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation });
}
