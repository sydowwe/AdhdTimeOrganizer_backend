using AdhdTimeOrganizer.application.service.morningBriefing;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.Core.domain.model.@enum;
using AdhdTimeOrganizer.infrastructure.persistence;
using AdhdTimeOrganizer.Routines.infrastructure.jobs;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.Contracts.scheduling;
using Sydowwe.Framework.Contracts.user;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.infrastructure.jobs;

/// <summary>
/// The morning briefing (P2-01): one notification per user per day, at their own wake-up, instead of a push per
/// morning signal — today's plan, the blocks left open on earlier days, the to-dos due today and overdue, the routine
/// items suggested for today. What it says is <see cref="MorningBriefingComposer"/>'s; this sweep decides when, and
/// whether at all.
/// <para>
/// <b>When — copied from <see cref="RoutineSuggestedTodayJobHandler"/>.</b> Every 15 minutes, because each user's
/// morning arrives at a different UTC instant and a run is how the sweep notices that one has. The morning is the
/// day's own <c>Calendar.WakeUpTime</c>, or the user's own <see cref="User.DefaultWakeUpTime"/> (FO10) when the day
/// has no row — the same resolution the routine digest uses, read in bulk for a page of users rather than through
/// <c>ICalendarDayLookup</c> and <c>UserSleepWindow</c> one user at a time, since the host can see the calendar. Plus
/// the user's own <see cref="User.MorningBriefingOffsetMinutes"/>.
/// </para>
/// <para>
/// <b>Decided once per morning.</b> The first run past the moment decides and marks
/// <see cref="User.MorningBriefingHandledFor"/>, whatever it decides: sent; nothing to say; an away day (vacation,
/// sick — a nudge on holiday is one the user learns to ignore); or the welcome-back card open, which <i>is</i> the
/// briefing for a returning user. So a block planned at 15:00 does not earn a "morning" push at 15:00. The one
/// exception is a dispatch that failed: that leaves the day open, and a later run retries — but only within
/// <see cref="LateBy"/> of the moment, past which it is no longer the morning's message.
/// </para>
/// <para>
/// <b>The routine digest steps aside</b> for a user with the briefing on (<see cref="User.MorningBriefingEnabled"/>,
/// read by the routine sweep itself). Held while away and quiet hours are the Notifications module's.
/// </para>
/// </summary>
public class MorningBriefingJobHandler(
    AppDbContext dbContext,
    IMorningBriefingNotifier notifier,
    IUserTimeZoneReader timeZoneReader,
    TimeProvider timeProvider,
    ILogger<MorningBriefingJobHandler> logger,
    // Defaulted, never registered — tests pass a small page to cross a page boundary.
    int userPageSize = MorningBriefingJobHandler.UserPageSize) : IScheduledJobHandler, IScopedService
{
    public const string HandlerKey = "Portal.MorningBriefing";

    public const int UserPageSize = 100;

    /// <summary>How long past its moment a briefing may still go out — the retry window after a failed dispatch.</summary>
    public static readonly TimeSpan LateBy = TimeSpan.FromHours(3);

    public string Key => HandlerKey;

    public async Task ExecuteAsync(ScheduledJobContext context, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var utcToday = DateOnly.FromDateTime(now);
        var sent = 0;
        long afterId = 0;

        while (true)
        {
            // Every zone's today is within a day of UTC's, so a mark for the day after UTC's today is a decided morning
            // anywhere. Compared for equality, here and per user below, like the routine digest's mark: a mark is a
            // date the user has had, and only the one naming their today says anything about this run.
            var page = await dbContext.Users
                .AsNoTracking()
                .Where(u => u.Id > afterId
                            && u.IsActive
                            && u.MorningBriefingEnabled
                            && (u.MorningBriefingHandledFor == null || u.MorningBriefingHandledFor != utcToday.AddDays(1)))
                .OrderBy(u => u.Id)
                .Take(userPageSize)
                .Select(u => new UserRow(
                    u.Id, u.MorningBriefingHandledFor, u.MorningBriefingOffsetMinutes, u.DefaultWakeUpTime,
                    u.WelcomeBackAwayFrom, u.WelcomeBackReturnedAt))
                .ToListAsync(ct);

            if (page.Count == 0)
                break;

            afterId = page[^1].Id;

            try
            {
                sent += await SweepPageAsync(page, now, utcToday, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // A page whose reads failed waits for the next run; the pages after it do not.
                logger.LogError(ex, "Morning briefing sweep failed for a page of {UserCount} users from {FirstUserId}", page.Count, page[0].Id);
            }

            if (page.Count < userPageSize)
                break;
        }

        if (sent > 0)
            logger.LogInformation("Morning briefing sweep sent {BriefingCount} briefings", sent);
    }

    private async Task<int> SweepPageAsync(List<UserRow> page, DateTime now, DateOnly utcToday, CancellationToken ct)
    {
        var userIds = page.Select(u => u.Id).ToList();
        var zones = await TimeZonesAsync(userIds, ct);

        var days = (await dbContext.Calendars
                .AsNoTracking()
                .Where(c => userIds.Contains(c.UserId) && c.Date >= utcToday.AddDays(-1) && c.Date <= utcToday.AddDays(1))
                .Select(c => new { c.UserId, c.Date, c.DayType, c.WakeUpTime })
                .ToListAsync(ct))
            .ToDictionary(c => (c.UserId, c.Date), c => (c.DayType, c.WakeUpTime));

        var decided = new List<(long UserId, DateOnly Date)>();
        var due = new Dictionary<long, MorningBriefingComposer.Morning>();

        foreach (var user in page)
        {
            // A user whose zone no longer resolves is absent from the map; UTC rather than a guess, as everywhere.
            var localNow = WallClockZone.FromUtc(now, zones.GetValueOrDefault(user.Id) ?? TimeZoneInfo.Utc);
            var today = DateOnly.FromDateTime(localNow);

            if (user.HandledFor == today)
                continue;

            (DayType DayType, TimeOnly WakeUpTime)? day = days.TryGetValue((user.Id, today), out var d) ? d : null;
            var sendAt = today.ToDateTime(day?.WakeUpTime ?? user.DefaultWakeUpTime)
                .AddMinutes(Math.Clamp(user.OffsetMinutes, 0, User.MorningBriefingMaxOffsetMinutes));

            if (localNow < sendAt)
                continue;

            if (localNow >= sendAt + LateBy
                || day?.DayType.IsAway() == true
                || (user.WelcomeBackAwayFrom is not null && UserPresence.CardIsOpen(user.WelcomeBackReturnedAt, now)))
            {
                decided.Add((user.Id, today));
                continue;
            }

            due[user.Id] = new MorningBriefingComposer.Morning(today, localNow);
        }

        var sent = 0;
        if (due.Count > 0)
        {
            var briefings = await MorningBriefingComposer.ComposeAsync(dbContext, due, now, ct);

            // Sequential: one scoped DbContext underneath the notifier, which writes the bell rows through it.
            foreach (var (userId, payload) in briefings)
            {
                if (MorningBriefingComposer.IsEmpty(payload))
                {
                    decided.Add((userId, payload.Date));
                    continue;
                }

                if (!await notifier.NotifyAsync(userId, payload, ct))
                    continue;

                // Marked the moment it went out: the mark is what stops the next run sending it again, so it must
                // not ride on the rest of the page succeeding.
                await MarkAsync([userId], payload.Date, ct);
                sent++;
            }
        }

        foreach (var group in decided.GroupBy(d => d.Date))
            await MarkAsync(group.Select(d => d.UserId).ToList(), group.Key, ct);

        return sent;
    }

    /// <summary>
    /// <c>ExecuteUpdate</c>, never a tracked <see cref="User"/>: the mark is not an edit, so it neither bumps the
    /// Identity concurrency stamp nor fails a preferences save that loaded the row a moment earlier.
    /// </summary>
    private Task MarkAsync(List<long> userIds, DateOnly date, CancellationToken ct) =>
        dbContext.Users
            .Where(u => userIds.Contains(u.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.MorningBriefingHandledFor, date), ct);

    /// <summary>
    /// The page's zones. <b>Fails open</b> to UTC for the whole page — a stored zone id that no longer materializes
    /// throws out of the read — so the page still gets its briefings, at UTC mornings, rather than none at all.
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
            logger.LogWarning(ex, "Could not read time zones for a page of {UserCount} users; briefing them on UTC", userIds.Count);
            return new Dictionary<long, TimeZoneInfo>();
        }
    }

    private sealed record UserRow(
        long Id, DateOnly? HandledFor, int OffsetMinutes, TimeOnly DefaultWakeUpTime, DateTime? WelcomeBackAwayFrom,
        DateTime? WelcomeBackReturnedAt);
}
