using AdhdTimeOrganizer.Core.application.seam;
using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.History.domain.model.@enum;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.config.dependencyInjection;

namespace AdhdTimeOrganizer.History.application.seam;

/// <summary>
/// Serves Core's <see cref="IFocusIntervalSource"/> from timer sessions: a Pomodoro's <c>Focus</c> rows and a
/// countdown's one row. Breaks, stopwatch, manual and tracker-attributed rows are left out — see the seam's remarks.
/// </summary>
/// <remarks>
/// Filters on <c>UserId</c> explicitly, as <see cref="ActivityHistoryTimeAttributionSink"/> does: the caller passes
/// the id it authenticated, and this must not answer for whoever happens to be ambient.
/// </remarks>
public sealed class ActivityHistoryFocusIntervalSource : IFocusIntervalSource, IScopedService
{
    public async Task<List<FocusInterval>> GetFocusIntervalsAsync(DbContext db, long userId, DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        // Npgsql rejects an Unspecified DateTime against a timestamptz column.
        var from = DateTime.SpecifyKind(fromUtc, DateTimeKind.Utc);
        var to = DateTime.SpecifyKind(toUtc, DateTimeKind.Utc);

        return await db.Set<ActivityHistory>()
            .AsNoTracking()
            .Where(h => h.UserId == userId && h.SessionId != null
                                           && (h.Phase == TimerSessionPhase.Focus
                                               || (h.Phase == null && h.Session!.Kind == TimerSessionKind.Timer))
                                           && h.StartTimestamp < to && h.EndTimestamp > from)
            .OrderBy(h => h.StartTimestamp)
            .Select(h => new FocusInterval(h.SessionId!.Value, h.ActivityId, h.StartTimestamp, h.EndTimestamp))
            .ToListAsync(ct);
    }
}
