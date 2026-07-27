using AdhdTimeOrganizer.Notifications.domain.entity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MojaDigitalnaFirma.Kernel.scheduling;
using Sydowwe.Framework.config.dependencyInjection;

namespace AdhdTimeOrganizer.Notifications.application.job;

// GDPR Art. 5(1)(e) (storage limitation) — retention for the Notifications module's history (audit
// 2026-07 finding L2; also windows L1's payload exposure, which was previously unbounded in time).
// `notification` and `push_subscription` are append-heavy and would otherwise grow for the life of the
// deployment, even though the bell UI only ever reads the newest 50 rows per user
// (GetMyNotificationsEndpoint) — everything older is dead weight carrying payload PII.
//
// Deletion rules (applied in this order, each independent and idempotent):
//   1. Read notifications older than ReadRetentionDays — the user has seen them; the bell will never
//      surface them again.
//   2. ALL notifications older than MaxRetentionDays, read or not. An unread notification a year old is
//      not a pending task, it is abandoned state; storage limitation outranks delivery here.
//   3. Push subscriptions whose ModifiedTimestamp is older than SubscriptionStaleDays. The SPA re-POSTs
//      its subscription on every app start and SubscribePushEndpoint.ApplyUpdateAsync force-touches the
//      row, so ModifiedTimestamp is a genuine last-seen instant — a row this old belongs to a device
//      that has not opened the app in half a year. Deleting it only costs a re-subscribe on next visit.
//
// The windows are policy, not statutory figures (owner decision 2026-07-06) — deliberately NOT listed in
// docs/routineLawCheckups.md. NotificationPreference is never touched: it is user *settings*, not
// history, and silently resetting an opt-out would re-enable notifications the user switched off.
//
// Hard delete via ExecuteDeleteAsync is deliberate: both entities are [NoAudit], so bypassing the
// ChangeTracker/audit interceptor loses nothing — and a retention purge must not itself write rows that
// re-create the data it just erased. Background-safe: no authenticated user is required, and only counts
// are logged (payloads and endpoints are PII / opaque secrets).
public class PurgeExpiredNotificationHistoryJobHandler(
    DbContext dbContext,
    ILogger<PurgeExpiredNotificationHistoryJobHandler> logger) : IScheduledJobHandler, IScopedService
{
    public const string HandlerKey = "Notifications.PurgeExpiredHistory";
    public const int ReadRetentionDays = 90;
    public const int MaxRetentionDays = 365;
    public const int SubscriptionStaleDays = 180;

    public string Key => HandlerKey;

    public Task ExecuteAsync(ScheduledJobContext context, CancellationToken ct) => PurgeExpiredAsync(ct);

    public async Task PurgeExpiredAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var readCutoff = now.AddDays(-ReadRetentionDays);
        var maxCutoff = now.AddDays(-MaxRetentionDays);
        var subscriptionCutoff = now.AddDays(-SubscriptionStaleDays);

        var notifications = dbContext.Set<Notification>();

        var readDeleted = await notifications
            .Where(n => n.IsRead && n.CreatedTimestamp < readCutoff)
            .ExecuteDeleteAsync(ct);

        // Runs after rule 1, so it only ever collects the unread remainder — the counts stay disjoint
        // and the log line reads as "seen and aged out" vs "abandoned".
        var expiredDeleted = await notifications
            .Where(n => n.CreatedTimestamp < maxCutoff)
            .ExecuteDeleteAsync(ct);

        var subscriptionsDeleted = await dbContext.Set<PushSubscription>()
            .Where(s => s.ModifiedTimestamp < subscriptionCutoff)
            .ExecuteDeleteAsync(ct);

        if (readDeleted > 0 || expiredDeleted > 0 || subscriptionsDeleted > 0)
            logger.LogInformation(
                "Notifications retention purge: deleted {ReadCount} read notification(s) past {ReadDays}d, "
                + "{ExpiredCount} notification(s) past {MaxDays}d, and {SubscriptionCount} push subscription(s) idle past {StaleDays}d",
                readDeleted, ReadRetentionDays, expiredDeleted, MaxRetentionDays, subscriptionsDeleted, SubscriptionStaleDays);
    }
}