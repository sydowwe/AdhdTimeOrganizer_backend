using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking.desktop;
using AdhdTimeOrganizer.Tracking.infrastructure.persistence.retention;
using Microsoft.Extensions.Options;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.Contracts.scheduling;

namespace AdhdTimeOrganizer.Tracking.infrastructure.jobs;

// GDPR Art. 5(1)(e) / SEC-4 & SEC-5 (review/portal/02-findings.md) — desktop_activity_entry,
// web_extension_activity_entry and android_session_data are append-only, per-heartbeat/per-minute/
// per-session ledgers holding PII (window titles, executable paths, browsing history, app usage) with no
// prior purge. WebExtensionActivityEntry's query filter (RecordDate >= CurrentPartitionDate) hides old
// rows from EF reads but was never a retention mechanism — this job is the actual deletion. Hard delete
// via ExecuteDeleteAsync: all three entities are technical ledgers, not user-authored records, so nothing
// here needs to survive as an audit trail. Background-safe (no authenticated user; every entity is read
// with IgnoreQueryFilters so the run purges every user's rows, not just an ambient one).
//
// android_session_data is the odd one out and was missed until SEC-3 (review/tracking/02-findings.md):
// it is not RANGE-partitioned on record_date like its two peers, so it has no DateOnly column to purge on
// and its predicate cannot prune partitions. It ages out on SessionStartUtc instead, which is a
// timestamptz — hence the DateTime cutoff below rather than the shared DateOnly one.
//
// A keyed IScheduledJobHandler rather than a Quartz IJob: the slice owns the retention rule, the
// Scheduler module owns the substrate, and this project references no Quartz. Its schedule is pushed on
// boot by TrackingScheduledJobsRegistrar.
public class PurgeExpiredActivityTrackingEntriesJobHandler(
    DbContext dbContext,
    IOptions<ActivityTrackingRetentionOptions> options,
    ILogger<PurgeExpiredActivityTrackingEntriesJobHandler> logger) : IScheduledJobHandler, IScopedService
{
    public const string HandlerKey = "Tracking.PurgeExpiredActivityTrackingEntries";

    public string Key => HandlerKey;

    public async Task ExecuteAsync(ScheduledJobContext context, CancellationToken ct)
    {
        var retention = options.Value;
        if (!retention.Enabled)
        {
            logger.LogDebug("Activity-tracking retention purge is disabled by configuration; skipping.");
            return;
        }

        // One instant for the whole run, so the three ledgers cannot disagree about where the horizon sits.
        // CutoffUtc() is DateTimeKind.Utc — required, since session_start_utc is timestamptz and Npgsql
        // refuses an Unspecified DateTime there in production (see CQ-1).
        var cutoffUtc = retention.CutoffUtc();
        var cutoffDate = DateOnly.FromDateTime(cutoffUtc);

        var deletedDesktop = await dbContext.Set<DesktopActivityEntry>()
            .IgnoreQueryFilters()
            .Where(e => e.RecordDate < cutoffDate)
            .ExecuteDeleteAsync(ct);

        var deletedWebExtension = await dbContext.Set<WebExtensionActivityEntry>()
            .IgnoreQueryFilters()
            .Where(e => e.RecordDate < cutoffDate)
            .ExecuteDeleteAsync(ct);

        var deletedAndroid = await dbContext.Set<AndroidSessionData>()
            .IgnoreQueryFilters()
            .Where(e => e.SessionStartUtc < cutoffUtc)
            .ExecuteDeleteAsync(ct);

        if (deletedDesktop > 0 || deletedWebExtension > 0 || deletedAndroid > 0)
            logger.LogInformation(
                "Purged {DesktopCount} desktop_activity_entry, {WebExtensionCount} web_extension_activity_entry and {AndroidCount} android_session_data row(s) past {Years}y retention",
                deletedDesktop, deletedWebExtension, deletedAndroid, retention.RetentionYears);
    }
}
