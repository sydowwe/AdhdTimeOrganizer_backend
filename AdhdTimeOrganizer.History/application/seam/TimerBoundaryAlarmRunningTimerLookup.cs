using AdhdTimeOrganizer.Core.application.seam;
using AdhdTimeOrganizer.History.domain.model.entity.timerAlarm;
using AdhdTimeOrganizer.History.domain.model.@enum;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.config.dependencyInjection;

namespace AdhdTimeOrganizer.History.application.seam;

/// <summary>
/// Serves Core's <see cref="IRunningTimerLookup"/> from the pending <see cref="TimerBoundaryAlarm"/> rows. The table
/// self-drains — a row goes when its alarm fires or its session is cancelled — so "a row whose instant is still
/// ahead" is "a timer is running". A stale row the boot registrar has not swept yet is in the past and does not count.
/// </summary>
/// <remarks>
/// <para>
/// Lifts the query filters and filters on <c>UserId</c> explicitly: the caller is a sweep with nobody signed in, and
/// names the users it is asking about.
/// </para>
/// <para>
/// <b>Phase boundaries only — a stopwatch's check-ins do not count.</b> The overrun nudge is held because a countdown
/// or a pomodoro rings at its own planned end. A stopwatch has no planned end, so the plan's "time's up" is the only
/// voice saying the slot is over, and its first check-in is 90 minutes out. Counting check-in rows would silently
/// switch the nudge off for everyone who times with a stopwatch.
/// </para>
/// </remarks>
public sealed class TimerBoundaryAlarmRunningTimerLookup : IRunningTimerLookup, IScopedService
{
    public async Task<IReadOnlySet<long>> GetUsersWithRunningTimerAsync(
        DbContext db, IReadOnlyCollection<long> userIds, DateTime utcNow, CancellationToken ct)
    {
        if (userIds.Count == 0)
            return new HashSet<long>();

        // Npgsql rejects an Unspecified DateTime against a timestamptz column.
        var now = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);

        return await db.Set<TimerBoundaryAlarm>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(a => userIds.Contains(a.UserId) && a.Kind == TimerAlarmKind.Boundary && a.BoundaryAt > now)
            .Select(a => a.UserId)
            .ToHashSetAsync(ct);
    }
}
