using AdhdTimeOrganizer.Routines.domain.model.@enum;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
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
    DbContext dbContext,
    TimeProvider timeProvider,
    ILogger<RoutinePeriodNotificationService> logger) : IRoutinePeriodNotificationService, IScopedService
{
    public async Task NotifyPeriodEndedAsync(
        RoutineTimePeriod period, RoutineResetService.RoutinePeriodReset reset, CancellationToken ct = default)
    {
        var (completion, outcome, isNewBest, previousBest) = reset;

        // The user was away for most of the cycle. "2 of 5 done" about a week spent ill is a guilt pile, not
        // information, and the streak did not move — so the summary is not sent at all, rather than mapped onto
        // the persisted payload contract, which has no "excused" member and should not grow one for silence.
        if (outcome == StreakOutcome.Excused)
            return;

        var streakBreak = outcome == StreakOutcome.Broken ? await DescribeBreakAsync(period, ct) : null;
        var extended = outcome == StreakOutcome.Extended;

        await SafeNotifyAsync(period, new RoutinePeriodEndedPayload(
            period.Id,
            period.Text,
            completion.CompletedCount,
            completion.TotalCount,
            period.Streak,
            ToKernelOutcome(outcome),
            LostStreak: streakBreak?.LostStreak ?? 0,
            FreezesRemaining: streakBreak?.FreezesRemaining ?? 0,
            CanFreeze: streakBreak?.CanFreeze ?? false,
            IsNewBest: isNewBest,
            PreviousBest: previousBest,
            // Only an extended cycle lands on a milestone. A held streak (grace) sits on the same number it
            // reached last cycle, which was already announced then.
            IsMilestone: extended && RoutineStreakMilestones.IsMilestone(period.Streak, period.LengthInDays),
            PeriodLengthInDays: period.LengthInDays), ct);
    }

    /// <summary>
    /// What the break lost and whether a freeze can still save it, so the summary can say "your 12-period run
    /// broke — 1 freeze left to save it" instead of only "broken" (the payload's streak is already 0). Read here,
    /// once, for every reset site — the nightly job and the two request paths that reset lazily — because all
    /// three call this after their commit, so the miss just written is the newest row.
    /// <para>
    /// The whole history, as <see cref="RoutineStreakFreezeService.Apply"/> reads it: the run reaches back past
    /// <c>HistoryDepth</c>. Only on a break, which is rare. <b>Fails open</b> to the plain "broken" sentence — a
    /// read that throws must not cost the user the summary itself.
    /// </para>
    /// </summary>
    private async Task<RoutineStreakFreezeService.StreakBreak?> DescribeBreakAsync(RoutineTimePeriod period, CancellationToken ct)
    {
        try
        {
            var history = await dbContext.Set<RoutinePeriodCompletion>()
                .AsNoTracking()
                .Where(c => c.TimePeriodId == period.Id)
                .OrderBy(c => c.PeriodStart)
                .ToListAsync(ct);

            return RoutineStreakFreezeService.DescribeBreak(period, history, timeProvider.GetUtcNow().UtcDateTime);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not read the completion history of period {PeriodId}; sending the plain summary", period.Id);
            return null;
        }
    }

    public async Task<bool> NotifyEndingSoonAsync(
        RoutineTimePeriod period, RoutineResetService.RoutineNudge nudge, int remaining, int total, CancellationToken ct = default)
    {
        // Nothing outstanding is not a notification. Reported as "not raised" so the sweep leaves the period
        // unmarked and re-evaluates it tomorrow, which is what makes un-ticking an item still earn a nudge.
        if (remaining <= 0)
            return false;

        return await SafeNotifyAsync(period, new RoutinePeriodEndingSoonPayload(
            period.Id,
            period.Text,
            remaining,
            total,
            nudge.DaysLeft,
            period.Streak), ct);
    }

    public async Task<bool> NotifyGraceExpiringAsync(RoutineTimePeriod period, CancellationToken ct = default)
    {
        return await SafeNotifyAsync(period, new RoutineStreakGraceExpiringPayload(
            period.Id,
            period.Text,
            period.StreakGraceUntil,
            period.Streak), ct);
    }

    public async Task<bool> NotifySuggestedTodayAsync(
        long userId, DateOnly date, IReadOnlyList<RoutineTodoList> items, CancellationToken ct = default)
    {
        // Nothing qualifies is not a notification — there is no "nothing today" message.
        if (items.Count == 0)
            return false;

        var payload = new RoutineSuggestedTodayPayload(
            date,
            items.Select(i => new RoutineSuggestedTodayItem(i.Id, i.Activity.Name, i.TimePeriodId)).ToList(),
            items.Count);

        return await SafeNotifyAsync(userId, payload, ct);
    }

    private Task<bool> SafeNotifyAsync<TPayload>(RoutineTimePeriod period, TPayload payload, CancellationToken ct)
        where TPayload : INotificationPayload =>
        SafeNotifyAsync(period.UserId, payload, ct);

    /// <summary>
    /// Best-effort dispatch. A reset that already mutated items and wrote a completion row must not be undone
    /// because SignalR or SMTP had a bad minute, and two of the three call sites are inside a request the user
    /// is waiting on. Logs the recipient's user id only — period and item names are user-authored and stay out
    /// of logs.
    /// <para>
    /// Returns false when the dispatch was swallowed, so a caller that stamps a "notified for" marker off the
    /// result never suppresses a notification that never went out.
    /// </para>
    /// </summary>
    private async Task<bool> SafeNotifyAsync<TPayload>(long userId, TPayload payload, CancellationToken ct)
        where TPayload : INotificationPayload
    {
        try
        {
            await notificationService.NotifyAsync(NotificationRecipients.User(userId), payload, ct);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Routine notification {Payload} failed for user {UserId}", typeof(TPayload).Name, userId);
            return false;
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