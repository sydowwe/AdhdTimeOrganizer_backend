using AdhdTimeOrganizer.Core.application.seam;
using AdhdTimeOrganizer.Core.domain.model.@enum;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Planning.domain.model.@enum;
using AdhdTimeOrganizer.Planning.domain.service;
using AdhdTimeOrganizer.Planning.domain.serviceContract;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.Contracts.scheduling;
using Sydowwe.Framework.Contracts.user;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.Planning.infrastructure.jobs;

/// <summary>
/// Audit N5's two time-blindness nudges, one sweep. It only notifies — it never writes a block's status; closing an
/// auto-complete block is <see cref="CompleteFinishedPlannerTasksJobHandler"/>'s, which is why the two stay apart.
/// <list type="bullet">
/// <item><b>Missed</b> — today's blocks that came and went unstarted (<see cref="PlannerTaskMissedRule"/>, the one
/// definition): "2 blocks slipped — re-plan the rest of today?". <b>One notification per user per sweep</b>, however
/// many blocks it found, and at most <see cref="MissedNotificationsPerDay"/> a day: a bad day must not become a stream
/// of reproaches. Nothing on an away day (vacation, sick day).</item>
/// <item><b>Overrun</b> — a started block still <c>InProgress</c> <see cref="OverrunGrace"/> past its end: "time's
/// up for X — wrap up or extend?". One per block end. Held while the user has a timer running
/// (<see cref="IRunningTimerLookup"/>): they are visibly working and the timer rings on its own; the nudge comes on
/// the first sweep after the timer stops, if the block is still open.</item>
/// </list>
/// <para>
/// <b>Today only, on the user's clock.</b> The query takes the three calendar dates around UTC today, which covers
/// every zone's today, and each user keeps only their own today — so a block left <c>InProgress</c> days ago, or one
/// missed last night, never pings. A block created after its own end (a template applied at noon, a block logged
/// after the fact) was never an attempt and is not "missed".
/// </para>
/// <para>
/// <b>Marks.</b> <see cref="PlannerTask.MissedNotifiedAt"/> / <see cref="PlannerTask.OverrunNotifiedAt"/>, stamped
/// with this sweep's instant only after the notification went out, and read as "announced" only while at or after
/// the block's <i>current</i> end — so a block re-planned later can be announced for its new slot. All blocks in one
/// missed notification share one stamp, which makes the day's distinct stamps the count the cap reads. Stamped with
/// <c>ExecuteUpdate</c>: a notification mark is not an edit, so it neither bumps the row's timestamps nor races the
/// user's own save on its version.
/// </para>
/// <para>
/// Unauthenticated: the query filters are lifted and every user is in scope, which is the point. Held while away and
/// quiet hours are the Notifications module's (the kind's <c>HeldWhileAway</c>, <c>DeferredUntil</c>), not this sweep's.
/// </para>
/// </summary>
public class PlannerSlipNudgeJobHandler(
    DbContext dbContext,
    IPlannerNotificationService notifier,
    IRunningTimerLookup runningTimers,
    IUserTimeZoneReader timeZoneReader,
    TimeProvider timeProvider,
    ILogger<PlannerSlipNudgeJobHandler> logger) : IScheduledJobHandler, IScopedService
{
    public const string HandlerKey = "Planning.SlipNudge";

    /// <summary>How many missed-block notifications a user gets for one day, at most.</summary>
    public const int MissedNotificationsPerDay = 2;

    /// <summary>How far past its end a started block must run before it counts as an overrun.</summary>
    public static readonly TimeSpan OverrunGrace = TimeSpan.FromMinutes(5);

    public string Key => HandlerKey;

    public async Task ExecuteAsync(ScheduledJobContext context, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var utcToday = DateOnly.FromDateTime(now);
        var from = utcToday.AddDays(-1);
        var to = utcToday.AddDays(1);

        // Open foreground blocks the sweep can act on, plus every block already announced as missed — completed or
        // not — because those stamps are what the daily cap counts. Served by ix_calendar_date.
        var rows = await dbContext.Set<PlannerTask>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(t => t.Calendar.Date >= from && t.Calendar.Date <= to && !t.IsBackground)
            .Where(t => t.Status == PlannerTaskStatus.NotStarted
                        || (t.Status == PlannerTaskStatus.InProgress && !t.AutoCompleteAtEnd)
                        || t.MissedNotifiedAt != null)
            .Select(t => new Block(
                t.Id, t.UserId, t.Calendar.Date, t.Calendar.DayType, t.StartTime, t.EndTime, t.Status, t.AutoCompleteAtEnd,
                t.CreatedTimestamp, t.MissedNotifiedAt, t.OverrunNotifiedAt, t.Activity.Name))
            .ToListAsync(ct);

        if (rows.Count == 0)
            return;

        var zones = await timeZoneReader.GetTimeZonesAsync(rows.Select(r => r.UserId).Distinct().ToList(), ct);

        var missed = new List<(long UserId, DateOnly Date, List<Block> Blocks)>();
        var overruns = new List<(Block Block, DateTime EndUtc)>();

        foreach (var userRows in rows.GroupBy(r => r.UserId))
        {
            var zone = zones.GetValueOrDefault(userRows.Key, TimeZoneInfo.Utc);
            var localNow = WallClockZone.FromUtc(now, zone);
            var localToday = DateOnly.FromDateTime(localNow);
            var today = userRows.Where(r => r.Date == localToday).ToList();

            if (FindMissed(today, localNow, zone) is { Count: > 0 } newlyMissed)
                missed.Add((userRows.Key, localToday, newlyMissed));

            overruns.AddRange(today
                .Where(b => b is { Status: PlannerTaskStatus.InProgress, AutoCompleteAtEnd: false })
                .Select(b => (Block: b, EndUtc: EndsAtUtc(b, zone)))
                .Where(o => o.EndUtc + OverrunGrace <= now && !(o.Block.OverrunNotifiedAt >= o.EndUtc)));
        }

        var missedSent = 0;
        foreach (var (userId, date, blocks) in missed)
        {
            var ordered = blocks.OrderBy(b => b.StartTime).ToList();
            if (!await notifier.NotifyBlocksMissedAsync(userId, date, ordered.Count, ordered[0].Id, ct))
                continue;

            await StampAsync(ordered.Select(b => b.Id).ToList(), missed: true, now, ct);
            missedSent++;
        }

        var overrunsSent = 0;
        if (overruns.Count > 0)
        {
            var timing = await runningTimers.GetUsersWithRunningTimerAsync(
                dbContext, overruns.Select(o => o.Block.UserId).Distinct().ToList(), now, ct);

            foreach (var (block, endUtc) in overruns.Where(o => !timing.Contains(o.Block.UserId)))
            {
                var minutesOver = (int)(now - endUtc).TotalMinutes;
                if (!await notifier.NotifyBlockOverrunAsync(block.UserId, block.Id, block.ActivityName, minutesOver, ct))
                    continue;

                await StampAsync([block.Id], missed: false, now, ct);
                overrunsSent++;
            }
        }

        if (missedSent + overrunsSent > 0)
            logger.LogInformation("Planner slip nudges: {Missed} missed-block and {Overrun} overrun notification(s) sent", missedSent, overrunsSent);
    }

    /// <summary>
    /// The user's blocks that slipped by since the last announcement, or none when the day is an away day or the cap
    /// is spent. Every block in <paramref name="today"/> is on one user's today.
    /// </summary>
    private static List<Block> FindMissed(List<Block> today, DateTime localNow, TimeZoneInfo zone)
    {
        if (today.Count == 0 || today[0].DayType.IsAway())
            return [];

        var sentToday = today.Where(b => b.MissedNotifiedAt != null).Select(b => b.MissedNotifiedAt).Distinct().Count();
        if (sentToday >= MissedNotificationsPerDay)
            return [];

        return today
            .Where(b => PlannerTaskMissedRule.IsMissed(b.Status, isBackground: false, b.Date, b.StartTime, b.EndTime, localNow))
            .Where(b =>
            {
                var endUtc = EndsAtUtc(b, zone);
                return b.CreatedTimestamp < endUtc && !(b.MissedNotifiedAt >= endUtc);
            })
            .ToList();
    }

    private async Task StampAsync(List<long> ids, bool missed, DateTime now, CancellationToken ct)
    {
        var tasks = dbContext.Set<PlannerTask>().IgnoreQueryFilters().Where(t => ids.Contains(t.Id));
        if (missed)
            await tasks.ExecuteUpdateAsync(s => s.SetProperty(t => t.MissedNotifiedAt, now), ct);
        else
            await tasks.ExecuteUpdateAsync(s => s.SetProperty(t => t.OverrunNotifiedAt, now), ct);
    }

    /// <summary>The instant the block's slot ends, on its owner's clock — <see cref="PlannerTaskMissedRule.EndsAt"/>'s wall clock, in UTC.</summary>
    private static DateTime EndsAtUtc(Block block, TimeZoneInfo zone) =>
        WallClockZone.ToUtc(PlannerTaskMissedRule.EndsAt(block.Date, block.StartTime, block.EndTime), zone);

    private sealed record Block(
        long Id,
        long UserId,
        DateOnly Date,
        DayType DayType,
        TimeOnly StartTime,
        TimeOnly EndTime,
        PlannerTaskStatus Status,
        bool AutoCompleteAtEnd,
        DateTime CreatedTimestamp,
        DateTime? MissedNotifiedAt,
        DateTime? OverrunNotifiedAt,
        string ActivityName);
}
