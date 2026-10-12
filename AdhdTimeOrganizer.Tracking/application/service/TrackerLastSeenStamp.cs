using AdhdTimeOrganizer.Tracking.domain.helper;
using AdhdTimeOrganizer.Tracking.domain.helper.unified;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking;
using AdhdTimeOrganizer.Tracking.domain.serviceContract;
using Microsoft.Extensions.Caching.Memory;
using Sydowwe.Framework.config.dependencyInjection;

namespace AdhdTimeOrganizer.Tracking.application.service;

/// <summary>
/// P2-04: the ingest side of "tracker went silent" — throttled the way <c>LastActiveStampMiddleware</c> stamps
/// <c>User.LastActiveAt</c>, because the same reasoning applies: the web-extension heartbeat is once a minute per
/// browser, and a write per heartbeat buys a precision the day-scale rule never reads.
/// <para>
/// <b>Throttled twice.</b> The in-memory key stops all but one request per hour per user and source reaching the
/// database at all; the conditional <c>UPDATE</c> stops a second instance (or a restart, which empties the cache)
/// from moving a stamp that is still fresh. Moving the stamp clears <see cref="TrackerSourceStatus.SilenceNotifiedAt"/>
/// in the same statement, which is what re-arms the notification for the next silence.
/// </para>
/// <para>
/// <b>The first report inserts</b>, and only then is the entity tracked — on the request's own scoped context, before
/// the endpoint has touched it. It is detached again whatever happens, so the endpoint's batch save never carries it:
/// a first report racing another (two trackers of one kind signing in at once) loses on the unique
/// <c>(user, source)</c> index here, and must not turn the batch into a 409.
/// </para>
/// </summary>
public class TrackerLastSeenStamp(
    DbContext dbContext,
    IMemoryCache throttle,
    TimeProvider time,
    ILogger<TrackerLastSeenStamp> logger) : ITrackerLastSeenStamp, IScopedService
{
    public async Task StampAsync(long userId, TrackingSource source, CancellationToken ct)
    {
        var key = $"p2-04-tracker-last-seen:{userId}:{source}";
        if (throttle.TryGetValue(key, out _))
            return;

        throttle.Set(key, true, TrackerSilenceRule.StampInterval);

        TrackerSourceStatus? inserted = null;
        try
        {
            var now = time.GetUtcNow().UtcDateTime;
            var staleBefore = now - TrackerSilenceRule.StampInterval;
            var statuses = dbContext.Set<TrackerSourceStatus>();

            var moved = await statuses
                .Where(s => s.UserId == userId && s.Source == source && s.LastSeenAt < staleBefore)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.LastSeenAt, now)
                    .SetProperty(x => x.SilenceNotifiedAt, (DateTime?)null), ct);

            if (moved > 0 || await statuses.AnyAsync(s => s.UserId == userId && s.Source == source, ct))
                return;

            inserted = new TrackerSourceStatus { UserId = userId, Source = source, LastSeenAt = now };
            statuses.Add(inserted);
            await dbContext.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not stamp the {Source} tracker as seen for user {UserId}; the ingest continues", source, userId);
        }
        finally
        {
            if (inserted is not null)
                dbContext.Entry(inserted).State = EntityState.Detached;
        }
    }
}
