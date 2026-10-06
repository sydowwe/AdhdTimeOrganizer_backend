using AdhdTimeOrganizer.Core.application.seam;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using AdhdTimeOrganizer.Routines.domain.service;
using AdhdTimeOrganizer.Routines.domain.serviceContract;
using Sydowwe.Framework.config.dependencyInjection;

namespace AdhdTimeOrganizer.Routines.application.service.routine;

/// <summary>
/// Pre-reads the away days a reset needs, so the pure <see cref="RoutineResetService"/> can apply
/// <see cref="RoutineAwayRule"/> without touching the database. Every reset site goes through here — the nightly
/// job, the grouped read, both toggles and the host's planner-task sync — because a site that skipped it would
/// score an away cycle as a miss, and the ledger is append-only: that verdict could only be repaired later by
/// the retroactive handler, and the item streaks not at all.
/// <para>
/// One read per call, however many periods and users it covers: the job calls it once per page of users.
/// Nothing is read when no period is due, which is the common case on the grouped read.
/// </para>
/// </summary>
public sealed class RoutineAwayDaysReader(ICalendarDayLookup calendarDayLookup) : IRoutineAwayDaysReader, IScopedService
{
    private static readonly IReadOnlyDictionary<long, IReadOnlySet<DateOnly>> None =
        new Dictionary<long, IReadOnlySet<DateOnly>>();

    public async Task<IReadOnlyDictionary<long, IReadOnlySet<DateOnly>>> ForDueCyclesAsync(
        DbContext dbContext, IEnumerable<RoutineTimePeriod> periods, DateTime now, CancellationToken ct)
    {
        DateOnly? from = null;
        var userIds = new HashSet<long>();

        foreach (var period in periods)
        {
            var due = RoutineResetService.ComputeNextReset(period, now);
            if (now < due)
                continue;

            // The oldest cycle a catch-up would close starts one length before the first due boundary; each
            // later one starts after it, so this is the earliest day any of them can contain.
            var start = DateOnly.FromDateTime(due).AddDays(-period.LengthInDays);
            if (from is null || start < from)
                from = start;
            userIds.Add(period.UserId);
        }

        if (from is not { } fromDate)
            return None;

        return await calendarDayLookup.GetAwayDatesAsync(dbContext, userIds, fromDate, DateOnly.FromDateTime(now), ct);
    }
}
