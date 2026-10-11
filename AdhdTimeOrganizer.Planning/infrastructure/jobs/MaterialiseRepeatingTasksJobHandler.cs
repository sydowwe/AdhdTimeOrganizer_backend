using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.Planning.application.service.taskPlanner;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Planning.domain.serviceContract;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.Contracts.scheduling;
using Sydowwe.Framework.Contracts.user;

namespace AdhdTimeOrganizer.Planning.infrastructure.jobs;

/// <summary>
/// Puts every auto-placing repeating rule's occurrences on the plan without anyone opening the app (FO6 × audit N1).
/// The materialiser otherwise runs only from a day-plan read and a rule's own write, so a user who did not open the
/// app that day got no block — and so no automatic reminder for it, on exactly the day they needed one.
/// <para>
/// <b>When.</b> Every 15 minutes, zone-aware: a user is due once their own <i>tomorrow</i> is past the watermark
/// (<see cref="RepeatingPlannerTask.MaterialisedThrough"/>) of one of their active rules — in practice the first tick
/// after their local midnight, which places the day after today. Today's blocks were placed the night before as
/// "tomorrow", so their reminders exist a full day ahead. The watermark is the "once per user per local day" mark:
/// after the call the rules are decided through tomorrow, and the user drops out of the candidates until the next
/// midnight. No mark of this job's own exists or is needed.
/// </para>
/// <para>
/// <b>Away users are skipped, not gated</b> (<see cref="UserPresence.IsAbsent"/>). Calling in for them would let the
/// materialiser decide their days and pass the watermark over them; skipping leaves those days undecided, so the
/// user's first day-plan read after the return places the blocks they came back for — see
/// <c>RepeatingTaskMaterialiser.IsUserAbsentAsync</c>.
/// </para>
/// <para>
/// <b>No signed-in user.</b> The global user filter is inert (every user is in scope, which is the point), and
/// nothing can stamp an owner onto a new row — the materialiser and <c>PlannedBlockAutoReminders</c> write every row
/// with an explicit <c>UserId</c>. Each user runs in a <b>scope of their own</b>, so one user's failed save cannot
/// leave staged entities for the next user's save to re-send; the materialiser also never throws and clears its
/// tracker on failure.
/// </para>
/// </summary>
public class MaterialiseRepeatingTasksJobHandler(
    DbContext dbContext,
    IServiceScopeFactory scopes,
    IUserTimeZoneReader timeZones,
    TimeProvider time,
    ILogger<MaterialiseRepeatingTasksJobHandler> logger) : IScheduledJobHandler, IScopedService
{
    public const string HandlerKey = "Planning.MaterialiseRepeatingTasks";

    public string Key => HandlerKey;

    public async Task ExecuteAsync(ScheduledJobContext context, CancellationToken ct)
    {
        var watermarks = await dbContext.Set<RepeatingPlannerTask>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(r => r.AutoPlace && r.IsActive)
            .Select(r => new { r.UserId, r.MaterialisedThrough })
            .ToListAsync(ct);

        if (watermarks.Count == 0)
            return;

        var now = time.GetUtcNow();
        var userIds = watermarks.Select(w => w.UserId).Distinct().ToList();

        var absentIfLastActiveBy = UserPresence.AbsentIfLastActiveBy(now.UtcDateTime);
        var absent = await dbContext.Set<User>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(u => userIds.Contains(u.Id) && u.LastActiveAt != null && u.LastActiveAt <= absentIfLastActiveBy)
            .Select(u => u.Id)
            .ToHashSetAsync(ct);

        var zones = await timeZones.GetTimeZonesAsync(userIds, ct);

        var due = watermarks
            .Where(w => !absent.Contains(w.UserId))
            .Where(w => w.MaterialisedThrough is not { } through
                        || through < LocalToday(now, zones.GetValueOrDefault(w.UserId, TimeZoneInfo.Utc)).AddDays(RepeatingTaskMaterialiser.DaysAhead))
            .Select(w => w.UserId)
            .Distinct()
            .ToList();

        foreach (var userId in due)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IRepeatingTaskMaterialiser>().PlaceDueAsync(userId, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                // The materialiser never throws; this is resolving it. Ids only.
                logger.LogError(ex, "Materialising repeating tasks failed for user {UserId}; the next tick retries", userId);
            }
        }

        if (due.Count > 0)
            logger.LogInformation("Materialised repeating tasks for {Count} user(s); {Absent} away user(s) skipped", due.Count, absent.Count);
    }

    private static DateOnly LocalToday(DateTimeOffset now, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
}
