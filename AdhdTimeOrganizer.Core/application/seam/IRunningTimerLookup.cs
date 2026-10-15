using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.Core.application.seam;

/// <summary>
/// Which users have a timer running right now, without the caller naming <c>TimerBoundaryAlarm</c>, which belongs to
/// History. Planning's overrun nudge (audit N5) holds "time's up for X" while one runs: the user is visibly working,
/// and the timer's own boundary alarms already ring — a second voice saying the plan's slot is over is noise. Reading
/// the alarm table directly would force a Planning → History reference. History implements this; Planning depends on
/// Core alone.
/// </summary>
/// <remarks>
/// <para>
/// <b>"Running" means a pending boundary alarm</b> — a row whose instant is still ahead. That is every server-known
/// timer with a planned end: a Pomodoro or countdown registers its alarms on Start and deletes them on Stop or when
/// they fire. A stopwatch registers check-ins on the same table, but those are not boundaries and are not counted:
/// a stopwatch has no planned end to ring, so the plan's nudge is still the only thing that says the slot is over.
/// </para>
/// <para>
/// Resolved as a <em>single</em> service, the same reasoning as <see cref="ICalendarDayLookup"/>: a missing
/// registration throws when the caller is activated instead of silently answering "nobody".
/// </para>
/// </remarks>
public interface IRunningTimerLookup : ISeam
{
    /// <summary>
    /// The subset of <paramref name="userIds"/> with a timer alarm still pending at <paramref name="utcNow"/>.
    /// Filtered on the given ids, never the ambient query filter: the caller is a sweep and runs unauthenticated.
    /// </summary>
    Task<IReadOnlySet<long>> GetUsersWithRunningTimerAsync(
        DbContext db, IReadOnlyCollection<long> userIds, DateTime utcNow, CancellationToken ct);
}
