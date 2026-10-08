using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.Core.application.seam;

/// <summary>
/// Reads the stretches a user <em>declared</em> they were focusing on an activity — timer focus phases — without
/// the caller naming <c>ActivityHistory</c>, which belongs to History. Tracking's mapping suggestions (FO9, F3-02)
/// learn which process was which activity from them; reading History directly would force a Tracking → History
/// reference. History implements this; Tracking depends on Core alone.
/// </summary>
/// <remarks>
/// <para>
/// <b>Only timer rows, never the whole ledger.</b> A Pomodoro's <c>Focus</c> rows and a countdown's single row are
/// the only intervals the user stated outright. Manual and stopwatch rows look exactly like tracker-attributed rows,
/// and those were themselves produced by the rules a suggestion would create — learning from them would feed the
/// rules back into themselves.
/// </para>
/// <para>
/// Resolved as a <em>single</em> service, the same reasoning as <see cref="ICalendarDayLookup"/>: a missing
/// registration throws at endpoint activation instead of silently suggesting nothing.
/// </para>
/// </remarks>
public interface IFocusIntervalSource : ISeam
{
    /// <summary>
    /// The user's timer focus intervals overlapping the half-open <c>[<paramref name="fromUtc"/>,
    /// <paramref name="toUtc"/>)</c>, oldest first.
    /// </summary>
    Task<List<FocusInterval>> GetFocusIntervalsAsync(DbContext db, long userId, DateTime fromUtc, DateTime toUtc, CancellationToken ct);
}

/// <param name="SessionId">The timer sitting the interval belongs to — what "seen in 7 of your timers" counts.</param>
/// <param name="ActivityId">The activity the user named for it.</param>
/// <param name="StartUtc">Start instant, UTC.</param>
/// <param name="EndUtc">End instant, UTC, exclusive.</param>
public sealed record FocusInterval(long SessionId, long ActivityId, DateTime StartUtc, DateTime EndUtc);
