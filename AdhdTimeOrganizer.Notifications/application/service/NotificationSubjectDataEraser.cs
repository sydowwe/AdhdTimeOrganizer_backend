using System.Linq.Expressions;
using AdhdTimeOrganizer.Notifications.domain.entity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MojaDigitalnaFirma.Kernel.gdpr;
using Sydowwe.Framework.config.dependencyInjection;

namespace AdhdTimeOrganizer.Notifications.application.service;

// Notifications' recipient-side slice of a GDPR Art. 17 erasure (notifications review L1/L2 — reminders
// follow-up 03). Reached through the Kernel ISubjectDataEraser fan-out, so EmployeeModule keeps no
// reference to this module. Mutates the ambient DbContext and never commits — the caller owns the
// transaction (see the contract's XML doc).
//
// ── Why DELETE here, where Reminders pseudonymizes ──────────────────────────────────────────────────
// None of these four tables is an append-only ledger. `notification` is plain bell history with no dedup
// invariant and no reversal lineage to protect (contrast reminder_dispatch / reminder_occurrence_action);
// preferences, quiet hours and push subscriptions are pure user settings. Nothing else references them, so
// removing the rows outright is both correct and the cleanest erasure.
//
// All four are genuinely in scope — no cascade does this for us. UserDeactivationService.DeactivateUserAsync
// only sets IsActive = false and rotates the security stamp; the user row is deliberately never deleted
// (ReactivateUserAsync needs it for the rehire/boomerang flow). So the FK-backed rows
// (NotificationPreference / PushSubscription, whose FK to the user is configured host-side) are not
// cascade-collected,
// and the FK-less ones (Notification.UserId, NotificationQuietHours.UserId) would simply linger.
// PushSubscription matters most: it holds a device endpoint plus the client's crypto keys.
//
// The retention purge (PurgeExpiredNotificationHistoryJobHandler) windows this history to <= 12 months by
// AGE; this closes the on-demand half — a subject anonymized today has last week's rows, which no age-based
// purge would touch for months.
//
// SCOPE — recipient-side only, i.e. strictly `WHERE UserId = <erased user>`. A notification *about* the
// erased employee sitting in someone else's bell is NOT touched: payloads are minimized to ids by
// INotificationPayloadEnricher and names are resolved at render time precisely so an anonymized employee
// degrades on its own, with no backfill and no payload scrubber. Do not turn this into one.
//
// All four entities are [NoAudit], but the deletes still go through the ChangeTracker rather than
// ExecuteDeleteAsync — that would commit immediately, outside the caller's transaction.
public class NotificationSubjectDataEraser(
    DbContext dbContext,
    ILogger<NotificationSubjectDataEraser> logger) : ISubjectDataEraser, IScopedService
{
    public string ModuleKey => "Notifications";

    public async Task<int> EraseSubjectDataAsync(SubjectErasureRequest request, CancellationToken ct)
    {
        var userId = request.UserId;
        // 0 is the "no authenticated user" sentinel the user-scoped write path guards against — never a real id.
        if (userId == 0)
            return 0;

        var notifications = await RemoveByUserAsync<Notification>(n => n.UserId == userId, ct);
        var preferences = await RemoveByUserAsync<NotificationPreference>(p => p.UserId == userId, ct);
        var subscriptions = await RemoveByUserAsync<PushSubscription>(s => s.UserId == userId, ct);
        var quietHours = await RemoveByUserAsync<NotificationQuietHours>(q => q.UserId == userId, ct);

        var total = notifications + preferences + subscriptions + quietHours;

        if (total > 0)
            logger.LogInformation(
                "Notifications subject erasure for employee {EmployeeId}: removed {Notifications} notification(s), "
                + "{Preferences} preference(s), {Subscriptions} push subscription(s) and {QuietHours} quiet-hours window(s)",
                request.EmployeeId, notifications, preferences, subscriptions, quietHours);

        return total;
    }

    private async Task<int> RemoveByUserAsync<TEntity>(
        Expression<Func<TEntity, bool>> ownedBySubject, CancellationToken ct)
        where TEntity : class
    {
        var rows = await dbContext.Set<TEntity>().Where(ownedBySubject).ToListAsync(ct);
        if (rows.Count == 0)
            return 0;

        dbContext.Set<TEntity>().RemoveRange(rows);
        return rows.Count;
    }
}