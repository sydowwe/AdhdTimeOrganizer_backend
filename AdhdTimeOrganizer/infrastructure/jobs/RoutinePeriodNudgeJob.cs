using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.domain.service;
using AdhdTimeOrganizer.domain.serviceContract;
using AdhdTimeOrganizer.infrastructure.persistence;
using Microsoft.EntityFrameworkCore;
using Quartz;

namespace AdhdTimeOrganizer.infrastructure.jobs;

/// <summary>
/// Daily sweep for the two mid-period routine notifications: the lead-time nudge about unfinished items, and
/// the warning that a streak's grace window is about to lapse.
/// <para>
/// <b>Why a sweep and not a registered reminder.</b> Every other scheduled thing in this solution goes through
/// the Reminders module, and the end-of-period summary is raised reactively by the reset itself. This one is
/// neither, on purpose: the body's whole value is "3 of 8 done", and a reminder payload is frozen at
/// registration time, so a registered reminder would announce yesterday's counts. Re-registering on every item
/// toggle to keep them fresh would be far more machinery than reading the rows once a day.
/// </para>
/// <para>
/// Runs at 09:00 rather than alongside <see cref="RoutineTodoListResetJob"/> at 02:00 — this one is addressed
/// to a person who is expected to act on it, not to the database.
/// </para>
/// </summary>
[DisallowConcurrentExecution]
public class RoutinePeriodNudgeJob(
    IServiceScopeFactory scopeFactory,
    ILogger<RoutinePeriodNudgeJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        var ct = context.CancellationToken;
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var notifier = scope.ServiceProvider.GetRequiredService<IRoutinePeriodNotificationService>();

        // No user filter: this runs unauthenticated, which is exactly when AppDbContext's per-user query
        // filter is inert, so every user's periods are in scope — as they must be for a sweep.
        // Hidden periods are skipped: the user has told us they are not looking at that routine right now.
        var periods = await dbContext.Set<RoutineTimePeriod>()
            .Where(p => !p.IsHidden)
            .Include(p => p.RoutineTodoListColl)
            .ToListAsync(ct);

        var now = DateTime.UtcNow;
        var nudged = 0;
        var graceWarned = 0;

        // Sequential on purpose. Fanning the notify calls out with Task.WhenAll would run them against
        // this scope's single AppDbContext concurrently — INotificationService writes notification rows
        // through the same scoped context — which throws rather than going faster. Doing it safely means
        // a scope (and DbContext) per period plus moving the marker writes off these tracked entities,
        // which is not worth it for a once-a-day sweep. See PERF-8 in review/portal/02-findings.md.
        foreach (var period in periods)
        {
            try
            {
                if (RoutineResetService.EvaluateEndingSoon(period, now) is { } nudge)
                {
                    var items = period.RoutineTodoListColl;
                    var remaining = items.Count(i => !i.IsDone);

                    // Marked only when something actually went out — see IRoutinePeriodNotificationService.
                    if (await notifier.NotifyEndingSoonAsync(period, nudge, remaining, items.Count, ct))
                    {
                        period.EndingSoonNotifiedFor = nudge.NextReset;
                        nudged++;
                    }
                }

                if (RoutineResetService.ShouldWarnGraceExpiring(period, now))
                {
                    await notifier.NotifyGraceExpiringAsync(period, ct);
                    period.GraceNotifiedFor = period.StreakGraceUntil;
                    graceWarned++;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // One period's failure must not lose the "notified for" markers already set for
                // earlier periods in this sweep, nor skip the periods still to come.
                logger.LogError(ex, "Routine nudge sweep failed for period {PeriodId}", period.Id);
            }
        }

        if (nudged == 0 && graceWarned == 0)
            return;

        await dbContext.SaveChangesAsync(ct);
        logger.LogInformation(
            "Routine nudge sweep sent {NudgeCount} lead-time nudges and {GraceCount} grace warnings", nudged, graceWarned);
    }
}