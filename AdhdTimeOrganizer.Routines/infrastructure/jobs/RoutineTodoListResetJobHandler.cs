using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using AdhdTimeOrganizer.Routines.domain.service;
using AdhdTimeOrganizer.Routines.domain.serviceContract;
using AdhdTimeOrganizer.Routines.infrastructure.persistence;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.Contracts.scheduling;

namespace AdhdTimeOrganizer.Routines.infrastructure.jobs;

/// <summary>
/// Daily reset of the routine to-do lists whose period has rolled over.
/// <para>
/// A keyed <see cref="IScheduledJobHandler"/>, not a Quartz <c>IJob</c>: this slice owns the work, the
/// Scheduler module owns the substrate (trigger, run log, retry, failure alerting). The schedule is
/// pushed on boot by <see cref="scheduling.RoutinesScheduledJobsRegistrar"/>.
/// </para>
/// <para>
/// Runs unauthenticated (background context), which is exactly when the host's per-user query filter is
/// inert — so every user's periods are in scope, as a sweep requires.
/// </para>
/// </summary>
public class RoutineTodoListResetJobHandler(
    DbContext dbContext,
    IRoutinePeriodNotificationService notifier,
    IRoutineAwayDaysReader awayDaysReader,
    ILogger<RoutineTodoListResetJobHandler> logger,
    // Defaulted, never registered: DI and the Scheduler's dispatcher both take the default, and the tests that
    // need the sweep to actually cross a page boundary pass 1 rather than seeding a hundred users.
    int userPageSize = RoutineSweepPaging.UserPageSize) : IScheduledJobHandler, IScopedService
{
    public const string HandlerKey = "Routines.TodoListReset";

    public string Key => HandlerKey;

    public async Task ExecuteAsync(ScheduledJobContext context, CancellationToken ct)
    {
        logger.LogInformation("RoutineTodoListResetJob started");

        var now = DateTime.UtcNow;
        var totalReset = 0;
        var periodsChanged = false;

        // Paged by user (see RoutineSweepPaging): loading every user's periods with two levels of collection
        // include put the whole table's item and step graph in memory at once.
        var pages = await RoutineSweepPaging.LoadUserPagesAsync(dbContext.Set<RoutineTimePeriod>(), userPageSize, ct);

        foreach (var userIds in pages)
        {
            var periods = await dbContext.Set<RoutineTimePeriod>()
                .Where(tp => userIds.Contains(tp.UserId))
                // Unfiltered by necessity: a filtered Include cannot correlate to the parent (`tp` is not
                // translatable inside the filter), so the owner check happens on `items` below instead.
                .Include(tp => tp.RoutineTodoListColl)
                .ThenInclude(t => t.Steps)
                .ToListAsync(ct);

            var reset = new List<(RoutineTimePeriod Period, RoutineResetService.RoutinePeriodReset Result)>();

            // One calendar read for the page: a cycle the user was mostly away for is excused, not missed.
            var awayDays = await awayDaysReader.ForDueCyclesAsync(dbContext, periods, now, ct);

            foreach (var period in periods)
            {
                // Owner-filtered (SEC-1). TimePeriodId is client-supplied on a routine item and the foreign key
                // enforces existence, not ownership, so a row can point at a period belonging to someone else —
                // the write validators refuse that now, but rows written before they existed are still there.
                // This job is exactly where such a row costs the victim something: it runs unauthenticated, so
                // the host's per-user query filter is inert and the include above returns every row aiming at
                // the period. One permanently unticked stranger's item is enough to drag completedCount /
                // items.Count below the threshold, breaking a streak whose cause the victim cannot see and
                // writing a RoutinePeriodCompletion that is wrong for good — the ledger is append-only.
                // Filtered here rather than in the Include because a filtered Include cannot reference `tp`.
                // Foreign rows are deliberately left alone rather than reset: they are not this period's to
                // touch, and silently mutating another user's data is the bug, not the fix.
                var items = period.RoutineTodoListColl.Where(t => t.UserId == period.UserId).ToList();
                var changed = RoutineResetService.CheckGrace(period, now);
                // The freeze budget refills lazily, so the nightly sweep is the one thing that brings a period
                // the user has not opened in a month back to a full budget before they look at it.
                changed |= RoutineStreakFreezeService.RefreshFreezeBudget(period, now);
                // Catch-up, not a single cycle: a period nobody opened for two months is closed out in this one
                // run and announced once, rather than dripping one retroactive miss per nightly run for two
                // months.
                var result = RoutineResetService.TryResetCatchUp(period, items, now, awayDays.GetValueOrDefault(period.UserId));
                if (result is { } r)
                {
                    dbContext.Set<RoutinePeriodCompletion>().AddRange(r.Completions);
                    changed = true;
                }

                if (!changed)
                    continue;

                try
                {
                    // One save per period, not one for the whole sweep or even for the whole page. A request
                    // that closed the same cycle a moment earlier collides here (see RoutineResetConflict);
                    // batched, that single collision aborted every period still to come and took the grace
                    // mutations already accumulated in the change tracker with it. DisallowConcurrent guards
                    // job-vs-job, not job-vs-request.
                    await dbContext.SaveChangesAsync(ct);
                }
                catch (Exception ex) when (RoutineResetConflict.IsLostRace(ex))
                {
                    logger.LogInformation(
                        "Period {PeriodId} was closed by a concurrent request; skipping it and continuing the sweep",
                        period.Id);
                    await DiscardAsync(period, items, result?.Completions, ct);
                    continue;
                }

                periodsChanged = true;
                if (result is { } committed)
                {
                    reset.Add((period, committed.Latest));
                    totalReset += items.Count;
                }
            }

            // After the commit, deliberately: the summary announces a reset that has happened, and a
            // notification failure must not be able to take the reset down with it (the notifier swallows, but
            // the ordering is the part that makes that safe). Per page rather than once at the end, so a page's
            // entities can be released as soon as everything that reads them is done.
            foreach (var (period, result) in reset)
                await notifier.NotifyPeriodEndedAsync(period, result, ct);

            // Safe here, unlike mid-page (see DiscardAsync): every period in this page has been saved and
            // announced, and the next page is read fresh rather than reused out of the tracker.
            dbContext.ChangeTracker.Clear();
        }

        if (!periodsChanged)
        {
            logger.LogInformation("No items to reset");
            return;
        }

        logger.LogInformation("Reset {Count} routine todo list items", totalReset);
    }

    /// <summary>
    /// Rolls one period's rejected attempt out of the change tracker, leaving every period still to come
    /// tracked. <c>ChangeTracker.Clear()</c> is what a request does here — it can afford to, because it is
    /// about to end — but in a sweep it would detach the periods not yet visited and their later saves would
    /// silently write nothing.
    /// </summary>
    private async Task DiscardAsync(
        RoutineTimePeriod period,
        List<RoutineTodoList> items,
        IReadOnlyList<RoutinePeriodCompletion>? completions,
        CancellationToken ct)
    {
        foreach (var completion in completions ?? [])
            dbContext.Entry(completion).State = EntityState.Detached;

        // Reload rather than detach: these carry the winner's committed values now, including the row_version
        // the failed save was checked against.
        await dbContext.Entry(period).ReloadAsync(ct);
        foreach (var item in items)
            await dbContext.Entry(item).ReloadAsync(ct);
    }
}
