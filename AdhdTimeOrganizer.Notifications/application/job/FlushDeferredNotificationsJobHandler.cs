using AdhdTimeOrganizer.Notifications.domain.entity;
using AdhdTimeOrganizer.Notifications.infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MojaDigitalnaFirma.Kernel.scheduling;
using Sydowwe.Framework.config.dependencyInjection;

namespace AdhdTimeOrganizer.Notifications.application.job;

// The back half of quiet hours (notifications follow-up 05). NotificationService withholds the BACKGROUND
// channels (Web Push, Email) for a recipient inside their NotificationQuietHours window and stamps the
// notification row with DeferredUntil — the instant the window ends. Nothing else re-reads that column, so
// without this sweep a deferred delivery would simply never arrive.
//
// The history row and the InApp (SignalR) push already went out at send time, so this job never creates,
// re-renders into, or mutates notification content: it only delivers the withheld channels and clears the
// column. The bell / `mine` list ignores DeferredUntil entirely.
//
// Cadence is every 15 minutes, so a window ending at 07:00 delivers by 07:15. The lag is deliberate and
// cheap: a per-user timer would be state this module has no substrate for, and the whole point of the
// feature is that these messages are NOT urgent. Idempotent — the column is cleared in the same save, so a
// re-run (misfire catch-up, double fire) finds nothing.
//
// Ordering note: deliver first, then clear. A crash between the two re-delivers on the next sweep rather
// than silently dropping the notification — the same at-least-once, best-effort posture the live dispatcher
// takes (a transport failure there is logged, never retried or surfaced).
public class FlushDeferredNotificationsJobHandler(
    DbContext dbContext,
    IDeferredNotificationDispatcher dispatcher,
    ILogger<FlushDeferredNotificationsJobHandler> logger) : IScheduledJobHandler, IScopedService
{
    public const string HandlerKey = "Notifications.FlushDeferredNotifications";

    /// <summary>
    /// Rows per run. Bounded so one sweep after a long outage (or a deployment-wide quiet window lifting at
    /// once) cannot fan out unboundedly; the remainder is picked up by the next run 15 minutes later.
    /// </summary>
    public const int BatchSize = 500;

    public string Key => HandlerKey;

    public Task ExecuteAsync(ScheduledJobContext context, CancellationToken ct) =>
        // The fire instant, not DateTimeOffset.UtcNow, so a run is deterministic and tests can drive it at a
        // controlled instant — the same convention ReminderScanJobHandler follows.
        // SpecifyKind, not the (DateTime, offset) ctor: the contract's fire time is UTC by name but carries no
        // guaranteed Kind, and that ctor throws when a Local-kind value meets a zero offset.
        FlushDeferredAsync(new DateTimeOffset(DateTime.SpecifyKind(context.ActualFireTimeUtc, DateTimeKind.Utc)), ct);

    public async Task FlushDeferredAsync(DateTimeOffset now, CancellationToken ct)
    {
        var due = await dbContext.Set<Notification>()
            .Where(n => n.DeferredUntil != null && n.DeferredUntil <= now)
            .OrderBy(n => n.DeferredUntil)
            .Take(BatchSize)
            .ToListAsync(ct);

        if (due.Count == 0)
            return;

        await dispatcher.DispatchDeferredAsync(due, ct);

        foreach (var notification in due)
            notification.DeferredUntil = null;

        await dbContext.SaveChangesAsync(ct);

        // Counts only — a payload is PII and a recipient id is not worth a line per row here.
        logger.LogInformation("Flushed {Count} deferred notification(s) whose quiet-hours window has ended", due.Count);
    }
}