using AdhdTimeOrganizer.Core.application.seam;
using AdhdTimeOrganizer.Core.domain.model.@enum;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using AdhdTimeOrganizer.Routines.domain.service;
using AdhdTimeOrganizer.Routines.domain.serviceContract;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.Contracts.scheduling;
using Sydowwe.Framework.Contracts.user;

namespace AdhdTimeOrganizer.Routines.infrastructure.jobs;

/// <summary>
/// The morning digest of the routine items suggested for the user's today and not done yet (backend ask B4).
/// One notification per user per day, sent once the user's morning has arrived in their own zone.
/// <para>
/// <b>Why every 15 minutes and not once a day.</b> "Today" and "morning" are the user's, not the server's: a
/// single UTC instant is Tuesday afternoon in Tokyo and still Monday night in Los Angeles, so a once-a-day run
/// names the wrong day for most of the world. Each run instead asks, per user, whether their local morning has
/// come and whether today's digest is still unsent; the first run that finds both true sends it.
/// </para>
/// <para>
/// <b>The morning</b> is the day's own <c>Calendar.WakeUpTime</c> when the user's day row exists — read through
/// Core's <see cref="ICalendarDayLookup"/>, since the calendar is Planning's — and
/// <see cref="DefaultSendAt"/> otherwise, the same 08:00 a lazily created day gets. No per-user setting.
/// </para>
/// <para>
/// <b>Not on an away day.</b> A user-local today marked vacation or sick gets no digest (FO1): a nudge on
/// holiday is one the user learns to ignore, and the habit outlasts the holiday.
/// </para>
/// <para>
/// <b>Composed at send time, like the nudge sweep</b> (see <see cref="RoutinePeriodNudgeJobHandler"/>): an item
/// ticked at 07:55 is not in an 08:00 digest. Only today's suggestions are named — an item suggested for
/// yesterday and still open is never mentioned again; <c>RoutinePeriodEndingSoon</c> covers running out of time.
/// </para>
/// <para>
/// <b>Marked only when something actually went out</b>, and per user rather than per page: the mark is what
/// stops the next run fifteen minutes later from sending the same digest again, so it is written the moment
/// the notification is raised. A swallowed dispatch leaves the day unmarked and a later run that morning
/// retries. Written with <c>ExecuteUpdate</c> over all of the user's periods, so the rows are never tracked,
/// cannot lose a row_version race to the user editing a period at 08:00, and a period created after this page
/// was read is covered too.
/// </para>
/// </summary>
public class RoutineSuggestedTodayJobHandler(
    DbContext dbContext,
    IRoutinePeriodNotificationService notifier,
    IUserTimeZoneReader timeZoneReader,
    ICalendarDayLookup calendarDayLookup,
    TimeProvider timeProvider,
    ILogger<RoutineSuggestedTodayJobHandler> logger,
    // Defaulted, never registered — see RoutineTodoListResetJobHandler's copy of this parameter.
    int userPageSize = RoutineSweepPaging.UserPageSize) : IScheduledJobHandler, IScopedService
{
    public const string HandlerKey = "Routines.SuggestedToday";

    /// <summary>When the digest goes out on a day the user has no calendar row for — a lazily created day's wake-up.</summary>
    public static readonly TimeOnly DefaultSendAt = new(8, 0);

    public string Key => HandlerKey;

    public async Task ExecuteAsync(ScheduledJobContext context, CancellationToken ct)
    {
        // Unauthenticated, so the per-user query filter is inert and every user is in scope. Only users who
        // could possibly qualify are paged: a visible, non-daily period with at least one item. Whether an item
        // names a day is decided in memory — SuggestedDays is a value-converted column, which EF cannot count.
        var candidates = dbContext.Set<RoutineTimePeriod>()
            .Where(p => !p.IsHidden && p.LengthInDays > 1 && p.RoutineTodoListColl.Any());
        var pages = await RoutineSweepPaging.LoadUserPagesAsync(candidates, userPageSize, ct);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var sent = 0;

        foreach (var userIds in pages)
        {
            // Every period of the page's users, hidden and daily included: those can never contribute an item,
            // but they carry the "already sent today" mark as much as any other period does.
            var periods = await dbContext.Set<RoutineTimePeriod>()
                .AsNoTracking()
                .Where(p => userIds.Contains(p.UserId))
                .Include(p => p.RoutineTodoListColl).ThenInclude(i => i.Activity)
                .ToListAsync(ct);
            var zones = await timeZoneReader.GetTimeZonesAsync(userIds, ct);

            // Sequential for the same reason as the nudge sweep: one scoped DbContext underneath the notifier.
            foreach (var userPeriods in periods.GroupBy(p => p.UserId))
            {
                try
                {
                    if (await TrySendAsync(userPeriods.Key, userPeriods.ToList(), zones, now, ct))
                        sent++;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // One user's failure must not cost the users still to come their digest.
                    logger.LogError(ex, "Routine suggested-today sweep failed for user {UserId}", userPeriods.Key);
                }
            }
        }

        if (sent > 0)
            logger.LogInformation("Routine suggested-today sweep sent {DigestCount} digests", sent);
    }

    private async Task<bool> TrySendAsync(
        long userId, List<RoutineTimePeriod> periods, IReadOnlyDictionary<long, TimeZoneInfo> zones, DateTime now, CancellationToken ct)
    {
        // A user whose zone no longer resolves is absent from the map; UTC rather than a guess, as everywhere.
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(now, zones.GetValueOrDefault(userId) ?? TimeZoneInfo.Utc);
        var today = DateOnly.FromDateTime(localNow);

        if (periods.Any(p => p.SuggestedTodayNotifiedFor == today))
            return false;

        var items = periods
            .Where(p => !p.IsHidden)
            .OrderBy(p => p.LengthInDays)
            .SelectMany(p => p.RoutineTodoListColl
                .Where(i => RoutineSuggestedDay.IsSuggestedOn(p, i, today) && !IsDoneThisCycle(p, i, now))
                .OrderBy(i => i.DisplayOrder))
            .ToList();

        // Nothing qualifies: nothing is sent and nothing is marked, so an item added later today still counts.
        if (items.Count == 0)
            return false;

        var day = (await calendarDayLookup.GetDaysAsync(dbContext, userId, today, today, ct)).FirstOrDefault();

        // A vacation or sick day is a quiet day. Nothing is sent and nothing is marked — the whole day is away,
        // so every later run today decides the same, and an un-marked day costs nothing tomorrow.
        if (day is not null && day.DayType.IsAway())
            return false;

        if (TimeOnly.FromDateTime(localNow) < (day?.WakeUpTime ?? DefaultSendAt))
            return false;

        if (!await notifier.NotifySuggestedTodayAsync(userId, today, items, ct))
            return false;

        await dbContext.Set<RoutineTimePeriod>()
            .Where(p => p.UserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.SuggestedTodayNotifiedFor, today), ct);
        return true;
    }

    /// <summary>
    /// Done <i>in the cycle that is still open</i>. Resets fall on midnight UTC but are only <i>applied</i> by the
    /// 02:00 UTC job or lazily on the next read, so at a user's morning the reset that closes last cycle may be
    /// due and not yet applied — an item ticked last Tuesday still reads <c>IsDone</c>. Past the due reset, that
    /// tick belongs to a closed cycle and says nothing about today. Before it, the tick is the current cycle's by
    /// the server's own definition, which is also what the routine list shows.
    /// </summary>
    private static bool IsDoneThisCycle(RoutineTimePeriod period, RoutineTodoList item, DateTime now) =>
        item.IsDone && now < RoutineResetService.ComputeNextReset(period, now);
}
