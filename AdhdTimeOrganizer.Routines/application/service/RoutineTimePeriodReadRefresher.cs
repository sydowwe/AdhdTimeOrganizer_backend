using AdhdTimeOrganizer.Routines.application.dto.response.todoList;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using AdhdTimeOrganizer.Routines.domain.service;

namespace AdhdTimeOrganizer.Routines.application.service.routine;

/// <summary>
/// Brings a set of periods current and renders them through <see cref="RoutineTimePeriodResponse.From"/>, for the
/// read endpoints that sit on the framework projection bases.
/// <para>
/// Those bases call <c>TResponse.Projection(query)</c> directly, and the projection is an expression tree — it
/// cannot compute <see cref="RoutineTimePeriodResponse.NextResetAt"/> or fill
/// <see cref="RoutineTimePeriodResponse.CompletionHistory"/>, and it cannot run the two lazy mutations the
/// period's own state depends on. So anything served straight off the projection reports an epoch
/// <c>NextResetAt</c>, an empty heatmap, a freeze count no read has refilled and a <c>Streak</c> whose grace
/// window may already have lapsed. This is the piece the <c>PostProcess</c> hooks put back.
/// </para>
/// <para>
/// <b>Both mutations are lazy by design and there is no job behind either</b>: whoever reads the value is the one
/// that has to bring it current (invariant 9 for the budget; the "CheckGrace on every access" rule for the
/// streak). They are persisted, not just projected — otherwise every read would hand out a fresh budget and
/// re-break the same streak.
/// </para>
/// <para>
/// Deliberately does <b>not</b> reset. A reset needs the period's items to score the cycle, and only the grouped
/// read, the toggle and the nightly job hold those; a flat read that reset would write a completion row from a
/// list it never loaded.
/// </para>
/// </summary>
public static class RoutineTimePeriodReadRefresher
{
    /// <summary>
    /// Refreshes every period in <paramref name="periodIds"/> and returns the rendered responses keyed by id.
    /// Ids the caller cannot see (or that no longer exist) are simply absent from the result, so a caller maps
    /// over its own ordered list rather than over this dictionary.
    /// </summary>
    public static async Task<Dictionary<long, RoutineTimePeriodResponse>> RefreshAndRenderAsync(
        DbContext dbContext,
        IEnumerable<long> periodIds,
        DateTime now,
        CancellationToken ct)
    {
        var ids = periodIds.Distinct().ToList();
        if (ids.Count == 0)
            return [];

        var periods = await dbContext.Set<RoutineTimePeriod>()
            .Where(p => ids.Contains(p.Id))
            .ToListAsync(ct);

        if (periods.Count == 0)
            return [];

        var changed = false;
        foreach (var period in periods)
        {
            changed |= RoutineResetService.CheckGrace(period, now);
            changed |= RoutineStreakFreezeService.RefreshFreezeBudget(period, now);
        }

        if (changed)
            await dbContext.SaveChangesAsync(ct);

        var completions = await dbContext.Set<RoutinePeriodCompletion>()
            .Where(c => ids.Contains(c.TimePeriodId))
            .OrderBy(c => c.TimePeriodId)
            .ThenByDescending(c => c.PeriodStart)
            .ToListAsync(ct);

        var completionsByPeriod = completions
            .GroupBy(c => c.TimePeriodId)
            .ToDictionary(g => g.Key, g => g.ToList());

        return periods.ToDictionary(
            p => p.Id,
            p => RoutineTimePeriodResponse.From(
                p,
                // Newest HistoryDepth rows, handed back oldest-first — the order From's contract asks for.
                completionsByPeriod.TryGetValue(p.Id, out var rows)
                    ? rows.Take(p.HistoryDepth).AsEnumerable().Reverse().ToList()
                    : [],
                now));
    }
}
