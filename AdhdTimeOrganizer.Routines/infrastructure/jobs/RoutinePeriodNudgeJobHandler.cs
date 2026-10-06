using AdhdTimeOrganizer.Core.application.seam;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using AdhdTimeOrganizer.Routines.domain.service;
using AdhdTimeOrganizer.Routines.domain.serviceContract;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.Contracts.scheduling;
using Sydowwe.Framework.Contracts.user;

namespace AdhdTimeOrganizer.Routines.infrastructure.jobs;

/// <summary>
/// Daily sweep for the two mid-period routine notifications: the lead-time nudge about unfinished items, and
/// the warning that a streak's grace window is about to lapse.
/// <para>
/// <b>Why a sweep and not a registered reminder.</b> Every other scheduled thing in this solution goes through
/// the Reminders module, and the end-of-period summary is raised reactively by the reset itself. This one is
/// neither, on purpose: the body's whole value is "3 of 8 done", and a reminder payload is frozen at
/// registration time, so a registered reminder would announce yesterday's counts. Re-registering on every item
/// toggle to keep them fresh would be far more machinery than reading the rows once a day.
/// </para>
/// <para>
/// A keyed <see cref="IScheduledJobHandler"/> rather than a Quartz <c>IJob</c> — this slice references no
/// Quartz; <see cref="scheduling.RoutinesScheduledJobsRegistrar"/> pushes the schedule on boot.
/// Runs at 09:00 rather than alongside <see cref="RoutineTodoListResetJobHandler"/> at 02:00 — this one is
/// addressed to a person who is expected to act on it, not to the database.
/// </para>
/// </summary>
public class RoutinePeriodNudgeJobHandler(
    DbContext dbContext,
    IRoutinePeriodNotificationService notifier,
    IUserTimeZoneReader timeZoneReader,
    ICalendarDayLookup calendarDayLookup,
    ILogger<RoutinePeriodNudgeJobHandler> logger,
    // Defaulted, never registered — see RoutineTodoListResetJobHandler's copy of this parameter.
    int userPageSize = RoutineSweepPaging.UserPageSize) : IScheduledJobHandler, IScopedService
{
    public const string HandlerKey = "Routines.PeriodNudge";

    public string Key => HandlerKey;

    public async Task ExecuteAsync(ScheduledJobContext context, CancellationToken ct)
    {
        // No user filter: this runs unauthenticated, which is exactly when AppDbContext's per-user query
        // filter is inert, so every user's periods are in scope — as they must be for a sweep.
        // Hidden periods are skipped: the user has told us they are not looking at that routine right now.
        // The same filter goes on the id query, so a user whose every period is hidden is never paged.
        var visible = dbContext.Set<RoutineTimePeriod>().Where(p => !p.IsHidden);
        var pages = await RoutineSweepPaging.LoadUserPagesAsync(visible, userPageSize, ct);

        var now = DateTime.UtcNow;
        var nudged = 0;
        var graceWarned = 0;

        foreach (var userIds in pages)
        {
            var periods = await dbContext.Set<RoutineTimePeriod>()
                .Where(p => !p.IsHidden && userIds.Contains(p.UserId))
                .Include(p => p.RoutineTodoListColl)
                .ToListAsync(ct);

            var pageNudged = 0;
            var pageGraceWarned = 0;
            var awayToday = await UsersAwayTodayAsync(userIds, now, ct);

            // Sequential on purpose. Fanning the notify calls out with Task.WhenAll would run them against
            // this scope's single AppDbContext concurrently — INotificationService writes notification rows
            // through the same scoped context — which throws rather than going faster. Doing it safely means
            // a scope (and DbContext) per period plus moving the marker writes off these tracked entities,
            // which is not worth it for a once-a-day sweep. See PERF-8 in review/portal/02-findings.md.
            foreach (var period in periods)
            {
                // Away today (vacation or sick, in the user's own calendar): no nudge and no grace warning, and
                // nothing marked — so a nudge still due tomorrow, once they are back, still goes out.
                if (awayToday.Contains(period.UserId))
                    continue;

                try
                {
                    if (RoutineResetService.EvaluateEndingSoon(period, now) is { } nudge)
                    {
                        var items = period.RoutineTodoListColl;
                        var remaining = items.Count(i => !i.IsDone);

                        // Marked only when something actually went out — see IRoutinePeriodNotificationService.
                        if (await notifier.NotifyEndingSoonAsync(period, nudge, remaining, items.Count, ct))
                        {
                            period.EndingSoonNotifiedFor = nudge.NextReset;
                            pageNudged++;
                        }
                    }

                    if (RoutineResetService.ShouldWarnGraceExpiring(period, now))
                    {
                        // Marked only when something actually went out — a swallowed dispatch that still
                        // stamped the marker would suppress the warning for the whole grace window, and the
                        // window would then lapse and break the streak with no warning ever sent.
                        if (await notifier.NotifyGraceExpiringAsync(period, ct))
                        {
                            period.GraceNotifiedFor = period.StreakGraceUntil;
                            pageGraceWarned++;
                        }
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // One period's failure must not lose the "notified for" markers already set for
                    // earlier periods in this sweep, nor skip the periods still to come.
                    logger.LogError(ex, "Routine nudge sweep failed for period {PeriodId}", period.Id);
                }
            }

            // Commit per page, not once for the whole sweep: the markers are what stop tomorrow's run
            // re-sending today's nudges, so a page's worth of them must not be riding on the last page's save.
            if (pageNudged > 0 || pageGraceWarned > 0)
                await dbContext.SaveChangesAsync(ct);

            nudged += pageNudged;
            graceWarned += pageGraceWarned;

            // Released once the page is committed — the sweep never holds more than UserPageSize users'
            // periods and items.
            dbContext.ChangeTracker.Clear();
        }

        if (nudged == 0 && graceWarned == 0)
            return;

        logger.LogInformation(
            "Routine nudge sweep sent {NudgeCount} lead-time nudges and {GraceCount} grace warnings", nudged, graceWarned);
    }

    /// <summary>
    /// The users in this page whose <b>own</b> today is an away day. The one zone-aware step in this sweep: the
    /// periods stay UTC end to end, but "today" in a calendar is the user's local date, as the suggested-today
    /// digest reads it. One zone read and one calendar read per page.
    /// <para>
    /// <b>Fails open.</b> This only ever suppresses a nudge, so if it cannot be answered — a stored zone id that
    /// no longer materializes throws out of the zone read, for one — the page is nudged exactly as it was before
    /// away days existed, rather than the whole sweep going down with it.
    /// </para>
    /// </summary>
    private async Task<HashSet<long>> UsersAwayTodayAsync(IReadOnlyCollection<long> userIds, DateTime now, CancellationToken ct)
    {
        if (userIds.Count == 0)
            return [];

        try
        {
            var zones = await timeZoneReader.GetTimeZonesAsync(userIds, ct);
            // A user whose zone no longer resolves is absent from the map; UTC rather than a guess, as everywhere.
            var todayByUser = userIds.ToDictionary(
                id => id,
                id => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(now, zones.GetValueOrDefault(id) ?? TimeZoneInfo.Utc)));

            var away = await calendarDayLookup.GetAwayDatesAsync(
                dbContext, userIds, todayByUser.Values.Min(), todayByUser.Values.Max(), ct);

            return todayByUser
                .Where(kv => away.TryGetValue(kv.Key, out var days) && days.Contains(kv.Value))
                .Select(kv => kv.Key)
                .ToHashSet();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not read away days for a page of {UserCount} users; nudging them as usual", userIds.Count);
            return [];
        }
    }
}
