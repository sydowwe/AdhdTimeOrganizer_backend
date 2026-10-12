using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.Tracking.domain.helper;
using AdhdTimeOrganizer.Tracking.domain.helper.unified;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking;
using AdhdTimeOrganizer.Tracking.domain.serviceContract;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.Contracts.scheduling;
using Sydowwe.Framework.Contracts.user;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.Tracking.infrastructure.jobs;

/// <summary>
/// P2-04: tells a user when one of their trackers has stopped reporting. Uninstalled, signed out, token expired,
/// crashed — none of it errors anywhere, the dashboards just go quiet, and the user concludes the feature is broken or
/// that they did nothing. The rule is <see cref="TrackerSilenceRule"/>'s; this sweep applies it.
/// <para>
/// <b>Hourly, and only in the user's waking day.</b> The rule already requires the user to have been in the web app
/// within a day, so this is a message for someone who is around — but "around yesterday evening" is no reason to buzz
/// them at 03:00. Each run asks, per silent source, whether it is between the user's own
/// <see cref="User.DefaultWakeUpTime"/> and <see cref="User.DefaultBedTime"/> in their zone (FO10's defaults — a
/// coarse "daytime" is all this needs, so the day's own calendar row is not read); the first run that finds it so
/// sends. A day-scale silence loses nothing by waiting for the morning.
/// </para>
/// <para>
/// <b>Once per silence.</b> <see cref="TrackerSourceStatus.SilenceNotifiedAt"/> is written the moment the notification
/// goes out, and only then — a swallowed dispatch leaves it open and the next hour retries. The mark is conditional on
/// the stamp the sweep read, so a report landing between the read and the mark wins and the source is left armed. The
/// next report clears it (<c>TrackerLastSeenStamp</c>), so a tracker that comes back and goes quiet again is told
/// about again.
/// </para>
/// <para>
/// Runs unauthenticated, so the user filter is lifted explicitly rather than relied on being inert. The candidate set
/// is small by construction — a silent source of a user present today — so it is read in one go, not paged.
/// </para>
/// </summary>
public class TrackerSilenceJobHandler(
    DbContext dbContext,
    ITrackerSilenceNotifier notifier,
    IUserTimeZoneReader timeZoneReader,
    TimeProvider timeProvider,
    ILogger<TrackerSilenceJobHandler> logger) : IScheduledJobHandler, IScopedService
{
    public const string HandlerKey = "Tracking.TrackerSilence";

    public string Key => HandlerKey;

    public async Task ExecuteAsync(ScheduledJobContext context, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var silentBefore = now - TrackerSilenceRule.SilentAfter;
        var reportedSince = now - TrackerSilenceRule.ReportedWithin;
        var presentSince = now - TrackerSilenceRule.PresentWithin;

        var silences = await (
                from status in dbContext.Set<TrackerSourceStatus>()
                join user in dbContext.Set<User>() on status.UserId equals user.Id
                where status.SilenceNotifiedAt == null
                      && status.LastSeenAt < silentBefore
                      && status.LastSeenAt >= reportedSince
                      && user.IsActive
                      && user.LastActiveAt >= presentSince
                orderby status.UserId, status.Id
                select new Silence(status.Id, status.UserId, status.Source, status.LastSeenAt, user.DefaultWakeUpTime, user.DefaultBedTime))
            .IgnoreQueryFilters()
            .AsNoTracking()
            .ToListAsync(ct);

        if (silences.Count == 0)
            return;

        var zones = await TimeZonesAsync(silences.Select(s => s.UserId).Distinct().ToList(), ct);
        var sent = 0;

        // Sequential: one scoped DbContext underneath the notifier, which writes the bell rows through it.
        foreach (var silence in silences)
        {
            try
            {
                if (await TrySendAsync(silence, zones, now, ct))
                    sent++;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // One source's failure must not cost the ones after it their notification.
                logger.LogError(ex, "Tracker-silence sweep failed for the {Source} tracker of user {UserId}", silence.Source, silence.UserId);
            }
        }

        if (sent > 0)
            logger.LogInformation("Tracker-silence sweep notified {SilenceCount} silent trackers", sent);
    }

    private async Task<bool> TrySendAsync(Silence silence, IReadOnlyDictionary<long, TimeZoneInfo> zones, DateTime now, CancellationToken ct)
    {
        // A user whose zone no longer resolves is absent from the map; UTC rather than a guess, as everywhere.
        var localNow = WallClockZone.FromUtc(now, zones.GetValueOrDefault(silence.UserId) ?? TimeZoneInfo.Utc);
        if (!TrackerSilenceRule.IsAwake(TimeOnly.FromDateTime(localNow), silence.WakeUpTime, silence.BedTime))
            return false;

        if (!await notifier.NotifyAsync(silence.UserId, silence.Source, silence.LastSeenAt, ct))
            return false;

        await dbContext.Set<TrackerSourceStatus>()
            .IgnoreQueryFilters()
            .Where(s => s.Id == silence.StatusId && s.SilenceNotifiedAt == null && s.LastSeenAt == silence.LastSeenAt)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.SilenceNotifiedAt, now), ct);
        return true;
    }

    /// <summary>
    /// The silent users' zones. <b>Fails open</b> to UTC — a stored zone id that no longer materializes throws out of
    /// the read — so the sweep still runs, on UTC days, rather than not at all.
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
            logger.LogWarning(ex, "Could not read time zones for {UserCount} users; judging their day on UTC", userIds.Count);
            return new Dictionary<long, TimeZoneInfo>();
        }
    }

    private sealed record Silence(
        long StatusId, long UserId, TrackingSource Source, DateTime LastSeenAt, TimeOnly WakeUpTime, TimeOnly BedTime);
}
