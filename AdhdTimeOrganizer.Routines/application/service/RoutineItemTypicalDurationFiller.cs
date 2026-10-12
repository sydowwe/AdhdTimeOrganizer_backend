using AdhdTimeOrganizer.Core.application.seam;
using AdhdTimeOrganizer.Routines.application.dto.response.todoList;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using AdhdTimeOrganizer.Routines.domain.service;

namespace AdhdTimeOrganizer.Routines.application.service.routine;

/// <summary>
/// Overlays F2-06's <see cref="RoutineTodoListResponse.TypicalSeconds"/> / <see cref="RoutineTodoListResponse.SampleCount"/>
/// onto already-rendered routine items. Two queries however many items: the periods' ledger boundaries, then the
/// recordings through <see cref="IRoutineItemLoggedTimeSource"/>. The rule itself is
/// <see cref="RoutineItemTypicalDuration"/>.
/// <para>
/// Every read that returns a routine item calls this — the grouped read, and both flat reads, because the list
/// re-reads a single item by id after logging time against it, and a figure that vanished on that re-read would
/// look like the log had erased it. Static, like TodoLists' <c>TodoListItemPlannedSlotFiller</c>: the endpoints
/// inject the seam themselves.
/// </para>
/// </summary>
public static class RoutineItemTypicalDurationFiller
{
    /// <summary>For a caller that has already loaded the items' periods (the grouped read).</summary>
    public static async Task<List<RoutineTodoListResponse>> FillAsync(
        List<RoutineTodoListResponse> items,
        IReadOnlyCollection<RoutineTimePeriod> periods,
        DbContext dbContext,
        IRoutineItemLoggedTimeSource loggedTime,
        long userId,
        DateTime now,
        CancellationToken ct)
    {
        if (items.Count == 0 || periods.Count == 0)
            return items;

        // Filtered on UserId explicitly: this is a module read, and the ledger row carries no user of its own.
        var periodIds = periods.Where(p => p.UserId == userId).Select(p => p.Id).ToList();
        var ledger = await dbContext.Set<RoutineTimePeriod>()
            .AsNoTracking()
            .Where(p => p.UserId == userId && periodIds.Contains(p.Id))
            .SelectMany(p => p.CompletionHistoryColl
                .OrderByDescending(c => c.PeriodStart)
                .Take(RoutineItemTypicalDuration.LookbackCycles)
                .Select(c => new { c.TimePeriodId, c.PeriodStart, c.PeriodEnd }))
            .ToListAsync(ct);

        var ledgerByPeriod = ledger
            .GroupBy(r => r.TimePeriodId)
            .ToDictionary(g => g.Key, g => g.Select(r => (r.PeriodStart, r.PeriodEnd)).ToList());

        var cyclesByPeriod = periods
            .Where(p => p.UserId == userId)
            .ToDictionary(
                p => p.Id,
                p => RoutineItemTypicalDuration.CyclesOf(p, ledgerByPeriod.GetValueOrDefault(p.Id, []), now));

        var measured = items.Where(i => cyclesByPeriod.ContainsKey(i.RoutineTimePeriod.Id)).ToList();
        if (measured.Count == 0)
            return items;

        var from = measured.Min(i => cyclesByPeriod[i.RoutineTimePeriod.Id].EarliestStartUtc);
        var to = measured.Max(i => cyclesByPeriod[i.RoutineTimePeriod.Id].Current.EndUtc);
        // A manual log may be dated later today than the request, and a reset already due ends before now.
        if (to < now.AddDays(1))
            to = now.AddDays(1);

        var recordings = await loggedTime.LoggedTimeBetweenAsync(
            dbContext, userId, measured.Select(i => i.Id).ToList(), from, to, ct);
        if (recordings.Count == 0)
            return items;

        var recordingsByItem = recordings.ToLookup(r => r.RoutineTodoListId);

        return items
            .Select(i =>
            {
                if (!cyclesByPeriod.TryGetValue(i.RoutineTimePeriod.Id, out var cycles))
                    return i;
                var typical = RoutineItemTypicalDuration.For(i.Id, cycles, i.IsDone, recordingsByItem[i.Id]);
                return typical is { } t
                    ? i with { TypicalSeconds = t.Seconds, SampleCount = t.SampleCount }
                    : i;
            })
            .ToList();
    }

    /// <summary>For the flat reads, which hold only the rendered items.</summary>
    public static async Task<List<RoutineTodoListResponse>> FillAsync(
        List<RoutineTodoListResponse> items,
        DbContext dbContext,
        IRoutineItemLoggedTimeSource loggedTime,
        long userId,
        DateTime now,
        CancellationToken ct)
    {
        if (items.Count == 0)
            return items;

        var periodIds = items.Select(i => i.RoutineTimePeriod.Id).Distinct().ToList();
        var periods = await dbContext.Set<RoutineTimePeriod>()
            .AsNoTracking()
            .Where(p => p.UserId == userId && periodIds.Contains(p.Id))
            .ToListAsync(ct);

        return await FillAsync(items, periods, dbContext, loggedTime, userId, now, ct);
    }
}
