using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking.desktop;
using AdhdTimeOrganizer.Tracking.infrastructure.persistence.retention;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Quartz;

namespace AdhdTimeOrganizer.Tracking.infrastructure.jobs;

// GDPR Art. 5(1)(e) / SEC-4 & SEC-5 (review/portal/02-findings.md) — desktop_activity_entry and
// web_extension_activity_entry are append-only, per-heartbeat/per-minute ledgers holding PII (window
// titles, executable paths, browsing history) with no prior purge. WebExtensionActivityEntry's query
// filter (RecordDate >= CurrentPartitionDate) hides old rows from EF reads but was never a retention
// mechanism — this job is the actual deletion. Hard delete via ExecuteDeleteAsync: both entities are
// per-heartbeat technical ledgers, not user-authored records, so nothing here needs to survive as an
// audit trail. Background-safe (no authenticated user; both entities are read with IgnoreQueryFilters
// so the run purges every user's rows, not just an ambient one).
[DisallowConcurrentExecution]
public class PurgeExpiredActivityTrackingEntriesJob(
    IServiceScopeFactory scopeFactory,
    IOptions<ActivityTrackingRetentionOptions> options,
    ILogger<PurgeExpiredActivityTrackingEntriesJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        var retention = options.Value;
        if (!retention.Enabled)
        {
            logger.LogDebug("Activity-tracking retention purge is disabled by configuration; skipping.");
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        // Plain DbContext, aliased to AppDbContext by the host — this slice must not name the host.
        var dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        var cutoffDate = DateOnly.FromDateTime(retention.CutoffUtc());
        var ct = context.CancellationToken;

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
