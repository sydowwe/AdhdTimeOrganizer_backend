using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking.desktop;
using AdhdTimeOrganizer.Tracking.infrastructure.persistence.retention;
using Microsoft.Extensions.Options;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.Contracts.scheduling;

namespace AdhdTimeOrganizer.Tracking.infrastructure.jobs;

// GDPR Art. 5(1)(e) / SEC-4 & SEC-5 (review/portal/02-findings.md) — desktop_activity_entry and
// web_extension_activity_entry are append-only, per-heartbeat/per-minute ledgers holding PII (window
// titles, executable paths, browsing history) with no prior purge. WebExtensionActivityEntry's query
// filter (RecordDate >= CurrentPartitionDate) hides old rows from EF reads but was never a retention
// mechanism — this job is the actual deletion. Hard delete via ExecuteDeleteAsync: both entities are
// per-heartbeat technical ledgers, not user-authored records, so nothing here needs to survive as an
// audit trail. Background-safe (no authenticated user; both entities are read with IgnoreQueryFilters
// so the run purges every user's rows, not just an ambient one).
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

        var cutoffDate = DateOnly.FromDateTime(retention.CutoffUtc());

        var deletedDesktop = await dbContext.Set<DesktopActivityEntry>()
            .IgnoreQueryFilters()
            .Where(e => e.RecordDate < cutoffDate)
            .ExecuteDeleteAsync(ct);

        var deletedWebExtension = await dbContext.Set<WebExtensionActivityEntry>()
            .IgnoreQueryFilters()
            .Where(e => e.RecordDate < cutoffDate)
            .ExecuteDeleteAsync(ct);

        if (deletedDesktop > 0 || deletedWebExtension > 0)
            logger.LogInformation(
                "Purged {DesktopCount} desktop_activity_entry and {WebExtensionCount} web_extension_activity_entry row(s) past {Years}y retention",
                deletedDesktop, deletedWebExtension, retention.RetentionYears);
    }
}
