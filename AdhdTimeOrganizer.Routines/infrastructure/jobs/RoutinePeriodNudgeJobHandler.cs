using AdhdTimeOrganizer.Core.application.seam;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using AdhdTimeOrganizer.Routines.domain.service;
using AdhdTimeOrganizer.Routines.domain.serviceContract;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.Contracts.scheduling;
using Sydowwe.Framework.Contracts.user;

namespace AdhdTimeOrganizer.Routines.infrastructure.jobs;

/// <summary>
/// The sweep for the two mid-period routine notifications: the lead-time nudge about unfinished items, and
/// the warning that a streak's grace window is about to lapse.
/// <para>
/// <b>Why a sweep and not a registered reminder.</b> Every other scheduled thing in this solution goes through
/// the Reminders module, and the end-of-period summary is raised reactively by the reset itself. This one is
/// neither, on purpose: the body's whole value is "3 of 8 done", and a reminder payload is frozen at
/// registration time, so a registered reminder would announce yesterday's counts. Re-registering on every item
/// toggle to keep them fresh would be far more machinery than reading the rows.
/// </para>
/// <para>
/// <b>Two clocks, kept apart.</b> <i>Whether</i> a nudge is due is UTC end to end — the windows are
/// <see cref="RoutineResetService.EvaluateEndingSoon"/> and <see cref="RoutineResetService.ShouldWarnGraceExpiring"/>,
/// bounded by UTC-midnight resets, and the <c>EndingSoonNotifiedFor</c> / <c>GraceNotifiedFor</c> marks are
/// compared against that freshly computed instant. <i>When</i> it reaches the user is theirs: the first
/// <see cref="DeliveryAfterWakeUp"/>-past-wake-up moment in their own zone that falls inside the window
/// (<see cref="DeliverAt"/>). It used to be one 09:00 UTC run for everyone — 05:00 in New York, 18:00 in Tokyo.
/// </para>
/// <para>
/// <b>Every 15 minutes</b>, like <see cref="RoutineSuggestedTodayJobHandler"/>, because each user's morning
/// arrives at a different UTC instant and a run is how the sweep notices that one has. The marks make the
/// cadence safe: the first run past a user's moment sends and marks, every later one finds the mark. The SQL
/// pre-filter (<see cref="Candidates"/>) keeps the other 95 runs a day from loading anything but the periods
/// whose window could be open and is unmarked.
/// </para>
/// <para>
/// A keyed <see cref="IScheduledJobHandler"/> rather than a Quartz <c>IJob</c> — this slice references no
/// Quartz; <see cref="scheduling.RoutinesScheduledJobsRegistrar"/> pushes the schedule on boot. Not alongside
/// <see cref="RoutineTodoListResetJobHandler"/> at 02:00 — this one is addressed to a person who is expected to
/// act on it, not to the database.
/// </para>
/// </summary>
public class RoutinePeriodNudgeJobHandler(
    DbContext dbContext,
    IRoutinePeriodNotificationService notifier,
    IUserTimeZoneReader timeZoneReader,
    ICalendarDayLookup calendarDayLookup,
    TimeProvider timeProvider,
    ILogger<RoutinePeriodNudgeJobHandler> logger,
    // Defaulted, never registered — see RoutineTodoListResetJobHandler's copy of this parameter.
    int userPageSize = RoutineSweepPaging.UserPageSize) : IScheduledJobHandler, IScopedService
{
    public const string HandlerKey = "Routines.PeriodNudge";

    /// <summary>
    /// How long after the day's wake-up the nudges go out — an hour behind the suggested-today digest, which
    /// goes at wake-up itself, so the morning's routine messages do not arrive as one pile.
    /// </summary>
    public static readonly TimeSpan DeliveryAfterWakeUp = TimeSpan.FromMinutes(60);

    public string Key => HandlerKey;

    public async Task ExecuteAsync(ScheduledJobContext context, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;

        // No user filter: this runs unauthenticated, which is exactly when AppDbContext's per-user query
        // filter is inert, so every user's periods are in scope — as they must be for a sweep. The same
        // candidate filter goes on the id query and the page fetch, so a user with nothing possibly due is
        // never paged.
        var candidates = Candidates(now);
        var pages = await RoutineSweepPaging.LoadUserPagesAsync(candidates, userPageSize, ct);

        var nudged = 0;
        var graceWarned = 0;

        foreach (var userIds in pages)
        {
            var periods = await candidates
                .Where(p => userIds.Contains(p.UserId))
                .Include(p => p.RoutineTodoListColl)
                .ToListAsync(ct);

            var zones = await TimeZonesAsync(userIds, ct);
            var awayToday = await UsersAwayTodayAsync(userIds, zones, now, ct);
            var pageNudged = 0;
            var pageGraceWarned = 0;

            // Sequential on purpose. Fanning the notify calls out with Task.WhenAll would run them against
            // this scope's single AppDbContext concurrently — INotificationService writes notification rows
            // through the same scoped context — which throws rather than going faster. Doing it safely means
            // a scope (and DbContext) per period plus moving the marker writes off these tracked entities,
            // which is not worth it for a sweep that sends a handful per run. See PERF-8 in
            // review/portal/02-findings.md.
            foreach (var userPeriods in periods.GroupBy(p => p.UserId))
            {
                // Away today (vacation or sick, in the user's own calendar): no nudge and no grace warning, and
                // nothing marked — so a nudge still due tomorrow, once they are back, still goes out.
                if (awayToday.Contains(userPeriods.Key))
                    continue;

                try
                {
                    var (sentNudges, sentGrace) = await SweepUserAsync(
                        userPeriods.Key, userPeriods.ToList(), zones.GetValueOrDefault(userPeriods.Key) ?? TimeZoneInfo.Utc, now, ct);
                    pageNudged += sentNudges;
                    pageGraceWarned += sentGrace;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // The wake-up read failed: this user waits for the next run, the rest of the page does not.
                    logger.LogError(ex, "Routine nudge sweep failed for user {UserId}", userPeriods.Key);
                }
            }

            // Commit per page, not once for the whole sweep: the markers are what stop the next run re-sending
            // these nudges, so a page's worth of them must not be riding on the last page's save.
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
    /// One user's due windows, each sent once the user's own delivery moment for it has come. The window math
    /// decides first and in UTC; only a window that is open and unmarked costs a calendar read.
    /// </summary>
    private async Task<(int Nudged, int GraceWarned)> SweepUserAsync(
        long userId, List<RoutineTimePeriod> periods, TimeZoneInfo zone, DateTime now, CancellationToken ct)
    {
        var due = periods
            .Select(p => (Period: p,
                Nudge: RoutineResetService.EvaluateEndingSoon(p, now),
                Grace: RoutineResetService.ShouldWarnGraceExpiring(p, now)))
            .Where(d => d.Nudge is not null || d.Grace)
            .ToList();

        if (due.Count == 0)
            return (0, 0);

        // Every window's opening day and the day after it, in the user's zone — DeliverAt never looks further.
        var opens = new List<DateTime>();
        foreach (var (period, nudge, grace) in due)
        {
            if (nudge is { } n)
                opens.Add(EndingSoonOpensAt(period, n));
            if (grace)
                opens.Add(GraceOpensAt(period));
        }

        var from = LocalDate(opens.Min(), zone);
        var to = LocalDate(opens.Max(), zone).AddDays(1);
        var wakeUps = (await calendarDayLookup.GetDaysAsync(dbContext, userId, from, to, ct))
            .ToDictionary(d => d.Date, d => d.WakeUpTime);

        var nudged = 0;
        var graceWarned = 0;

        foreach (var (period, nudge, grace) in due)
        {
            try
            {
                if (nudge is { } n && now >= DeliverAt(EndingSoonOpensAt(period, n), n.NextReset, zone, wakeUps))
                {
                    var items = period.RoutineTodoListColl;
                    var remaining = items.Count(i => !i.IsDone);

                    // Marked only when something actually went out — see IRoutinePeriodNotificationService.
                    if (await notifier.NotifyEndingSoonAsync(period, n, remaining, items.Count, ct))
                    {
                        period.EndingSoonNotifiedFor = n.NextReset;
                        nudged++;
                    }
                }

                if (grace && now >= DeliverAt(GraceOpensAt(period), period.StreakGraceUntil!.Value, zone, wakeUps))
                {
                    // Marked only when something actually went out — a swallowed dispatch that still stamped the
                    // marker would suppress the warning for the whole grace window, and the window would then
                    // lapse and break the streak with no warning ever sent.
                    if (await notifier.NotifyGraceExpiringAsync(period, ct))
                    {
                        period.GraceNotifiedFor = period.StreakGraceUntil;
                        graceWarned++;
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // One period's failure must not lose the "notified for" markers already set for earlier
                // periods in this sweep, nor skip the periods still to come.
                logger.LogError(ex, "Routine nudge sweep failed for period {PeriodId}", period.Id);
            }
        }

        return (nudged, graceWarned);
    }

    /// <summary>
    /// When a window's notification reaches the user: the first wake-up + <see cref="DeliveryAfterWakeUp"/> in
    /// their zone at or after <paramref name="windowOpen"/>, from the day's calendar row, or
    /// <see cref="RoutineSuggestedTodayJobHandler.DefaultSendAt"/> on a day without one (the wake-up a lazily
    /// created day gets — the same resolution the suggested-today digest uses).
    /// <para>
    /// Both windows are at least a day long, so that moment falls inside one unless the wake-up moves later
    /// from one day to the next across the window's end. Then the window would close before any moment came,
    /// and the answer is <paramref name="windowOpen"/> itself: a nudge at an odd hour beats a streak that
    /// breaks with no warning at all.
    /// </para>
    /// <para>
    /// A moment already behind <c>now</c> is simply due. That is the catch-up path: a period skipped unmarked
    /// because everything was done earns its nudge on the first run after an item is un-ticked, whatever the
    /// hour — the window may close before the next morning.
    /// </para>
    /// </summary>
    private static DateTime DeliverAt(
        DateTime windowOpen, DateTime windowClose, TimeZoneInfo zone, IReadOnlyDictionary<DateOnly, TimeOnly> wakeUps)
    {
        var openDate = LocalDate(windowOpen, zone);

        // A moment lies within [date 00:00, date+1 01:00) local, so the day after the opening day always has
        // one past windowOpen — two days are the whole search.
        foreach (var date in (DateOnly[])[openDate, openDate.AddDays(1)])
        {
            var at = MomentOn(date, zone, wakeUps);
            if (at >= windowOpen)
                return at < windowClose ? at : windowOpen;
        }

        return windowOpen;
    }

    private static DateTime MomentOn(DateOnly date, TimeZoneInfo zone, IReadOnlyDictionary<DateOnly, TimeOnly> wakeUps)
    {
        var wakeUp = wakeUps.TryGetValue(date, out var w) ? w : RoutineSuggestedTodayJobHandler.DefaultSendAt;
        var local = date.ToDateTime(wakeUp).Add(DeliveryAfterWakeUp);

        // A wall-clock time inside a spring-forward gap never happens; the first one after the gap stands in.
        while (zone.IsInvalidTime(local))
            local = local.AddMinutes(15);

        return TimeZoneInfo.ConvertTimeToUtc(local, zone);
    }

    private static DateTime EndingSoonOpensAt(RoutineTimePeriod period, RoutineResetService.RoutineNudge nudge) =>
        nudge.NextReset.AddDays(-period.ReminderLeadDays!.Value);

    private static DateTime GraceOpensAt(RoutineTimePeriod period) =>
        period.StreakGraceUntil!.Value.AddDays(-RoutineResetService.GraceWarningLeadDays);

    private static DateOnly LocalDate(DateTime utc, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, zone));

    /// <summary>
    /// The visible periods that could have a window open and unmarked at <paramref name="now"/> — a necessary
    /// condition, never a sufficient one: the window math in memory still decides. Hidden periods are skipped
    /// (the user has told us they are not looking at that routine right now).
    /// <list type="bullet">
    /// <item><b>Lead-time nudge.</b> A lead is set; the mark is not for a reset still ahead (a mark is only ever
    /// that reset, and an edit or reset clears it); and the window can have opened. The next reset is never
    /// earlier than <c>(last reset + length).Date</c>, so its window cannot open before
    /// <c>last reset + length − lead − 1 day</c> — except for the calendar-month and calendar-year anchors,
    /// whose next reset follows the calendar rather than the length, and which are always let through.</item>
    /// <item><b>Grace warning.</b> Exact: the grace instant is ahead, within the warning lead, and unwarned.</item>
    /// </list>
    /// </summary>
    private IQueryable<RoutineTimePeriod> Candidates(DateTime now)
    {
        var graceHorizon = now.AddDays(RoutineResetService.GraceWarningLeadDays);

        return dbContext.Set<RoutineTimePeriod>().Where(p => !p.IsHidden && (
            (p.ReminderLeadDays != null
             && (p.EndingSoonNotifiedFor == null || p.EndingSoonNotifiedFor <= now)
             && ((p.ResetAnchorDay != 0 && (p.LengthInDays == 30 || p.LengthInDays == 365))
                 || (p.LastResetAt ?? p.CreatedTimestamp).AddDays(p.LengthInDays - p.ReminderLeadDays.Value - 1) <= now))
            || (p.StreakGraceUntil != null
                && p.StreakGraceUntil > now
                && p.StreakGraceUntil <= graceHorizon
                && (p.GraceNotifiedFor == null || p.GraceNotifiedFor != p.StreakGraceUntil))));
    }

    /// <summary>
    /// The page's zones. <b>Fails open</b> to UTC for the whole page — a stored zone id that no longer
    /// materializes throws out of the read — so the page is still nudged, at UTC mornings, rather than the
    /// sweep going down with it.
    /// </summary>
    private async Task<IReadOnlyDictionary<long, TimeZoneInfo>> TimeZonesAsync(IReadOnlyCollection<long> userIds, CancellationToken ct)
    {
        try
        {
            return await timeZoneReader.GetTimeZonesAsync(userIds, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not read time zones for a page of {UserCount} users; nudging them on UTC", userIds.Count);
            return new Dictionary<long, TimeZoneInfo>();
        }
    }

    /// <summary>
    /// The users in this page whose <b>own</b> today is an away day — "today" in a calendar is the user's local
    /// date, as the suggested-today digest reads it. One calendar read per page.
    /// <para>
    /// <b>Fails open.</b> This only ever suppresses a nudge, so if it cannot be answered the page is nudged
    /// exactly as it was before away days existed, rather than the whole sweep going down with it.
    /// </para>
    /// </summary>
    private async Task<HashSet<long>> UsersAwayTodayAsync(
        IReadOnlyCollection<long> userIds, IReadOnlyDictionary<long, TimeZoneInfo> zones, DateTime now, CancellationToken ct)
    {
        if (userIds.Count == 0)
            return [];

        try
        {
            // A user whose zone no longer resolves is absent from the map; UTC rather than a guess, as everywhere.
            var todayByUser = userIds.ToDictionary(id => id, id => LocalDate(now, zones.GetValueOrDefault(id) ?? TimeZoneInfo.Utc));

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
