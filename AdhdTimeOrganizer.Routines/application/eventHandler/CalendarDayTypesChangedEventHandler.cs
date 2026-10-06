using AdhdTimeOrganizer.Core.application.@event;
using AdhdTimeOrganizer.Core.application.seam;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using AdhdTimeOrganizer.Routines.domain.service;
using FastEndpoints;

namespace AdhdTimeOrganizer.Routines.application.eventHandler;

/// <summary>
/// Excuses routine cycles <b>after the fact</b> when the user marks the days they covered as vacation or sick.
/// <para>
/// Nobody pre-marks a sick day, and the usual flow is to come back first and fix the calendar afterwards — by
/// which point the cycles the absence covered have already been closed and scored as misses. This re-checks the
/// closed cycles that contain a changed date against <see cref="RoutineAwayRule"/>, sets
/// <see cref="RoutinePeriodCompletion.IsExcused"/> where it now holds, clears the open grace window when the
/// newly excused cycle is the newest one (only that one can own it, exactly as a freeze does), and re-walks the
/// streak. <see cref="RoutineStreakFreezeService.RecomputeStreak"/> only raises, so a streak broken last Tuesday
/// comes back.
/// </para>
/// <para>
/// <b>Known limits, by design.</b> Item streaks cannot come back: an item has no history to re-derive one from.
/// And excusal only ever goes one way — marking a day back to a workday leaves a cycle it excused excused, because
/// un-excusing would cut a streak retroactively, the one outcome this feature exists to prevent.
/// </para>
/// <para>
/// Lives in Routines, the owner of every row it touches; the publisher is Planning, and neither references the
/// other — the event is declared in Core and the away days are read through <see cref="ICalendarDayLookup"/>.
/// Resolves its own scope because the publisher's is not guaranteed to outlive the dispatch, and logs and
/// swallows, like every cross-slice handler: the day-type change has already committed, and must not 500
/// because of a streak.
/// </para>
/// </summary>
public class CalendarDayTypesChangedEventHandler(
    IServiceScopeFactory scopeFactory,
    ILogger<CalendarDayTypesChangedEventHandler> logger) : IEventHandler<CalendarDayTypesChangedEvent>
{
    public async Task HandleAsync(CalendarDayTypesChangedEvent eventModel, CancellationToken ct)
    {
        if (eventModel.Dates.Count == 0)
            return;

        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        var calendarDayLookup = scope.ServiceProvider.GetRequiredService<ICalendarDayLookup>();

        try
        {
            var changed = eventModel.Dates.ToHashSet();
            var minDate = changed.Min();
            var maxDate = changed.Max();

            // Scoped on the event's UserId, never on the ambient filter, which is inert without a principal. A
            // cycle is [PeriodStart, PeriodEnd), so it holds a changed date when it starts on or before the last
            // one and ends after the first one; the exact membership is checked in memory below.
            var periods = await dbContext.Set<RoutineTimePeriod>()
                .Where(p => p.UserId == eventModel.UserId)
                .Where(p => p.CompletionHistoryColl.Any(c =>
                    c.PeriodStart <= maxDate && c.PeriodEnd > minDate
                    && !c.IsFrozen && !c.IsExcused && c.TotalCount > 0))
                .ToListAsync(ct);

            if (periods.Count == 0)
                return;

            var periodIds = periods.Select(p => p.Id).ToList();
            // The whole history, oldest first: RecomputeStreak walks the run, and a truncated one under-counts it.
            var histories = (await dbContext.Set<RoutinePeriodCompletion>()
                    .Where(c => periodIds.Contains(c.TimePeriodId))
                    .OrderBy(c => c.PeriodStart)
                    .ToListAsync(ct))
                .GroupBy(c => c.TimePeriodId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var candidates = periods
                .SelectMany(p => histories.GetValueOrDefault(p.Id, []))
                .Where(c => ContainsAny(c, changed))
                .ToList();

            if (candidates.Count == 0)
                return;

            // One calendar read for every candidate cycle: the rule needs all of each cycle's days, not only the
            // ones that changed.
            var awayDays = (await calendarDayLookup.GetAwayDatesAsync(
                    dbContext,
                    [eventModel.UserId],
                    candidates.Min(c => c.PeriodStart),
                    candidates.Max(c => c.PeriodEnd).AddDays(-1),
                    ct))
                .GetValueOrDefault(eventModel.UserId);

            var now = DateTime.UtcNow;
            var excused = 0;

            foreach (var period in periods)
            {
                var history = histories.GetValueOrDefault(period.Id, []);
                var toExcuse = history
                    .Where(c => ContainsAny(c, changed) && RoutineAwayRule.CanExcuse(c, period.StreakThreshold, awayDays))
                    .ToList();

                if (toExcuse.Count == 0)
                    continue;

                // Before touching the window, like the freeze endpoint: a window that has already lapsed is not
                // live, and leaving it standing would let the next read zero the streak this is about to restore.
                RoutineResetService.CheckGrace(period, now);

                foreach (var completion in toExcuse)
                {
                    completion.IsExcused = true;

                    // The newest cycle is the only one that can own the open grace window, and an excused cycle
                    // is not a miss — so the window has nothing left to protect.
                    if (ReferenceEquals(completion, history[^1]))
                    {
                        period.StreakGraceUntil = null;
                        period.GraceNotifiedFor = null;
                    }
                }

                RoutineStreakFreezeService.RecomputeStreak(period, history);
                excused += toExcuse.Count;
            }

            if (excused == 0)
                return;

            await dbContext.SaveChangesAsync(ct);

            logger.LogInformation(
                "Excused {CycleCount} routine cycles retroactively for user {UserId}", excused, eventModel.UserId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to excuse routine cycles for user {UserId} after a day-type change", eventModel.UserId);
        }
    }

    private static bool ContainsAny(RoutinePeriodCompletion completion, IReadOnlySet<DateOnly> dates) =>
        dates.Any(d => d >= completion.PeriodStart && d < completion.PeriodEnd);
}
