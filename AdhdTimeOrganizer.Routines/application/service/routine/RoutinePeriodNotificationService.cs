using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using AdhdTimeOrganizer.Core.domain.model.@enum;
using AdhdTimeOrganizer.Routines.domain.service;
using AdhdTimeOrganizer.Routines.domain.serviceContract;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.Contracts.notification;
using Sydowwe.Framework.Contracts.notification.payload;

namespace AdhdTimeOrganizer.Routines.application.service.routine;

/// <summary>
/// The portal's routine-domain notification producer. Maps <c>RoutineTimePeriod</c> events onto the
/// <c>Sydowwe.Framework.Contracts</c> notification contract; nothing else in the portal names a routine <c>NotificationType</c> or payload.
/// <para>
/// Recipient is always the period's own <c>UserId</c> — a routine belongs to exactly one person, so there is
/// no resolver and no fan-out. The user's channel choices are applied downstream by the Notifications module,
/// so this class does not consult preferences.
/// </para>
/// </summary>
public class RoutinePeriodNotificationService(
    INotificationService notificationService,
    ILogger<RoutinePeriodNotificationService> logger) : IRoutinePeriodNotificationService, IScopedService
{
    public async Task NotifyPeriodEndedAsync(
        RoutineTimePeriod period, RoutinePeriodCompletion completion, StreakOutcome outcome, CancellationToken ct = default)
    {
        await SafeNotifyAsync(period, new RoutinePeriodEndedPayload(
            period.Id,
            period.Text,
            completion.CompletedCount,
            completion.TotalCount,
            period.Streak,
            ToKernelOutcome(outcome)), ct);
    }

    public async Task<bool> NotifyEndingSoonAsync(
        RoutineTimePeriod period, RoutineResetService.RoutineNudge nudge, int remaining, int total, CancellationToken ct = default)
    {
        // Nothing outstanding is not a notification. Reported as "not raised" so the sweep leaves the period
        // unmarked and re-evaluates it tomorrow, which is what makes un-ticking an item still earn a nudge.
        if (remaining <= 0)
            return false;

        await SafeNotifyAsync(period, new RoutinePeriodEndingSoonPayload(
            period.Id,
            period.Text,
            remaining,
            total,
            nudge.DaysLeft,
            period.Streak), ct);

        return true;
    }

    public async Task NotifyGraceExpiringAsync(RoutineTimePeriod period, CancellationToken ct = default)
    {
        await SafeNotifyAsync(period, new RoutineStreakGraceExpiringPayload(
            period.Id,
            period.Text,
            period.StreakGraceUntil,
            period.Streak), ct);
    }

    /// <summary>
    /// Best-effort dispatch. A reset that already mutated items and wrote a completion row must not be undone
    /// because SignalR or SMTP had a bad minute, and two of the three call sites are inside a request the user
    /// is waiting on. Logs the period id only — the period's <c>Text</c> is user-authored and stays out of logs.
    /// </summary>
    private async Task SafeNotifyAsync<TPayload>(RoutineTimePeriod period, TPayload payload, CancellationToken ct)
        where TPayload : INotificationPayload
    {
        try
        {
            await notificationService.NotifyAsync(NotificationRecipients.User(period.UserId), payload, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Routine notification {Payload} failed for period {PeriodId}", typeof(TPayload).Name, period.Id);
        }
    }

    private static RoutineStreakOutcome ToKernelOutcome(StreakOutcome outcome) => outcome switch
    {
        StreakOutcome.Extended => RoutineStreakOutcome.Extended,
        StreakOutcome.OnGrace => RoutineStreakOutcome.OnGrace,
        StreakOutcome.Broken => RoutineStreakOutcome.Broken,
        // An empty period says nothing about the streak, and the renderer's Unknown branch is exactly the
        // "say nothing about the streak" branch — the same silence, reached honestly.
        _ => RoutineStreakOutcome.Unknown
    };
}