using AdhdTimeOrganizer.Core.domain.model.@enum;
using AdhdTimeOrganizer.Core.domain.serviceContract;
using AdhdTimeOrganizer.Planning.application.dto.@enum;
using AdhdTimeOrganizer.Planning.application.helper;
using AdhdTimeOrganizer.Planning.domain.model.entity;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Planning.domain.model.entity.reminder;
using AdhdTimeOrganizer.Planning.domain.model.@enum;
using AdhdTimeOrganizer.Planning.domain.service;
using AdhdTimeOrganizer.Planning.domain.serviceContract;
using Sydowwe.Framework.config.dependencyInjection;

namespace AdhdTimeOrganizer.Planning.application.service.taskPlanner;

/// <summary>
/// FO6 — recurring commitments put themselves on the plan. See <see cref="IRepeatingTaskMaterialiser"/> for the
/// contract; the rules it keeps are these.
/// <list type="bullet">
/// <item><b>Today and tomorrow only</b>, on the user's own clock (<see cref="DaysAhead"/>). Never a past day, and
/// never further out: an occurrence is a block the user did not choose today, so it appears no sooner than the
/// day before.</item>
/// <item><b>Forward only, once per day.</b> A rule's <see cref="RepeatingPlannerTask.MaterialisedThrough"/>
/// records the last date decided, and nothing at or before it is looked at again — so an occurrence the user
/// deleted or skipped stays deleted or skipped. On top of that, a day that already holds an occurrence of the
/// rule (whatever its status, and however it got there — say, yesterday's carried over) receives no second one.
/// There is deliberately no unique index behind that dedupe; see <c>PlannerTaskConfiguration</c>.</item>
/// <item><b>Conflicts follow <c>UserPlannerSettings.DefaultConflictResolution</c></b>, through
/// <see cref="PlannerTemplateApplier.StageAgainstDay"/> — the one implementation of the four modes. A
/// destructive mode can delete or carve the user's own blocks here exactly as a template apply would, which is
/// why the deleted tasks' reminders are captured before the save and cancelled after it.</item>
/// <item><b>Gates.</b> An away day (FO1: <c>Vacation</c>, <c>SickDay</c>) is passed over, as is every day while
/// <see cref="IsUserAbsentAsync"/> says the user is gone (FO5's hook, unbuilt). A passed-over day is still
/// decided: marking the day as a workday again later does not bring the occurrence back. The suggestion panel
/// still offers the rule on that day.</item>
/// <item><b>Concurrency.</b> Home and the planner can read the same day at once. Both stage the same
/// occurrences, and both advance the same rule rows; the rule's <c>row_version</c> lets exactly one save through,
/// and the loser's whole save — occurrences included — rolls back.</item>
/// </list>
/// <para>
/// <b>No reminders.</b> No planner-task create registers one today (audit N1 is unbuilt), and occurrences are no
/// exception. When N1 lands, this is a create path it has to cover.
/// </para>
/// <para>
/// <b>FO3 needs nothing here.</b> An occurrence starts with <c>PostponeCount = 0</c> and no original slot; moving
/// one later through the PUT or a drag is counted by <c>PlannerTaskMoveRecorder</c> like any other task.
/// </para>
/// <para>
/// Scoped by hand (<c>IgnoreQueryFilters()</c> + explicit <c>UserId</c>), the same as <see cref="CalendarProvisioner"/>:
/// it is told which user to act for, and the answer must not depend on who is signed in.
/// </para>
/// </summary>
public sealed class RepeatingTaskMaterialiser(
    DbContext dbContext,
    ICalendarProvisioner calendars,
    IUserTimeZoneResolver timeZones,
    IReminderRegistrationService reminders,
    ILogger<RepeatingTaskMaterialiser> logger)
    : IRepeatingTaskMaterialiser, IScopedService
{
    /// <summary>How far past today occurrences are placed: tomorrow, and no further.</summary>
    public const int DaysAhead = 1;

    /// <summary>
    /// What a user without a settings row gets — <c>UserPlannerSettings</c>' own default. The settings row is
    /// created on the first <c>GET /planner/settings</c>, so a user who has never opened the planner has none.
    /// </summary>
    private const ApplyTemplateConflictResolutionEnum ConflictResolutionWithoutSettings = ApplyTemplateConflictResolutionEnum.Ignore;

    public async Task PlaceForReadAsync(long userId, DateOnly readDate, CancellationToken ct = default)
    {
        await RunSafelyAsync(userId, "placing", async () =>
        {
            var today = await TodayAsync(userId, ct);
            if (readDate >= today && readDate <= today.AddDays(DaysAhead))
                await PlaceDueCoreAsync(userId, today, ct);
        }, ct);
    }

    public async Task PlaceDueAsync(long userId, CancellationToken ct = default)
    {
        await RunSafelyAsync(userId, "placing",
            async () => await PlaceDueCoreAsync(userId, await TodayAsync(userId, ct), ct), ct);
    }

    public async Task ReconcileAfterEditAsync(RepeatingPlannerTask rule, RepeatingPlannerTask before, CancellationToken ct = default)
    {
        await RunSafelyAsync(rule.UserId, "reconciling an edited rule for",
            async () => await ReconcileCoreAsync(rule, before, await TodayAsync(rule.UserId, ct), ct), ct);
    }

    // ------------------------------------------------------------------ placing

    private async Task PlaceDueCoreAsync(long userId, DateOnly today, CancellationToken ct)
    {
        var lastDay = today.AddDays(DaysAhead);

        // The cheap probe every day-plan read pays. Once the day's rules are decided it matches nothing, so the
        // steady state is one indexed query and no write.
        if (!await DueRules(userId, lastDay).AnyAsync(ct))
            return;

        var userAbsent = await IsUserAbsentAsync(userId, ct);

        // Provisioning commits, so every day is resolved before anything of ours is staged.
        for (var date = today; date <= lastDay; date = date.AddDays(1))
            await calendars.EnsureForDateAsync(userId, date, ct);

        var days = await LoadDaysAsync(userId, today, lastDay, ct);
        var rules = await DueRules(userId, lastDay).ToListAsync(ct);
        var conflictResolution = await ConflictResolutionAsync(userId, ct);

        var placed = 0;
        for (var date = today; date <= lastDay; date = date.AddDays(1))
        {
            var calendar = days[date];
            var occurrences = new List<PlannerTask>();

            foreach (var rule in rules.Where(r => r.MaterialisedThrough is null || r.MaterialisedThrough < date))
            {
                if (!IsGated(calendar, userAbsent)
                    && RepeatingPlannerTaskSchedule.FallsOn(rule, date, calendar.DayType)
                    && !HoldsOccurrenceOf(calendar, rule.Id))
                    occurrences.Add(OccurrenceOf(rule, calendar));

                // Decided either way — placed, already there, not this rule's day, or gated.
                rule.MaterialisedThrough = date;
            }

            if (occurrences.Count == 0)
                continue;

            PlannerTemplateApplier.StageAgainstDay(dbContext, calendar, conflictResolution, occurrences);
            placed += occurrences.Count;
        }

        await SaveAndCancelOrphanedRemindersAsync(ct);

        if (placed > 0)
            logger.LogInformation("Placed {Count} repeating-task occurrence(s) for user {UserId} on {From}..{Until}",
                placed, userId, today, lastDay);
    }

    /// <summary>
    /// Active auto-placing rules with a day left to decide in the window. <c>IsActive</c> gates placing, not
    /// reconciling: a paused rule keeps its watermark where it stopped, and resuming it places from today.
    /// </summary>
    private IQueryable<RepeatingPlannerTask> DueRules(long userId, DateOnly lastDay) =>
        dbContext.Set<RepeatingPlannerTask>()
            .IgnoreQueryFilters()
            .Where(r => r.UserId == userId && r.AutoPlace && r.IsActive
                        && (r.MaterialisedThrough == null || r.MaterialisedThrough < lastDay));

    /// <summary>
    /// FO1 and FO5's gates. A gated day is decided like any other, so the watermark passes over it.
    /// </summary>
    private static bool IsGated(Calendar calendar, bool userAbsent) => userAbsent || calendar.DayType.IsAway();

    /// <summary>
    /// <b>FO5 hook — unbuilt.</b> FO5 adds <c>User.LastActiveAt</c> and one shared "is the user away?" check;
    /// when it lands, answer from that here, and a user it sees as absent gets no auto-placed blocks. Until
    /// then nobody is absent: this runs from the user's own reads, which is the user being present.
    /// </summary>
    private static Task<bool> IsUserAbsentAsync(long userId, CancellationToken ct) => Task.FromResult(false);

    private static bool HoldsOccurrenceOf(Calendar calendar, long ruleId) =>
        calendar.Tasks.Any(t => t.SourceRepeatingTaskId == ruleId);

    /// <summary>
    /// A fresh <c>NotStarted</c> block carrying the rule's plan: span, lane, location, notes, activity and
    /// importance. No execution state, no to-do link, no original slot — the same shape a copy has.
    /// </summary>
    private static PlannerTask OccurrenceOf(RepeatingPlannerTask rule, Calendar calendar) =>
        new()
        {
            UserId = rule.UserId,
            CalendarId = calendar.Id,
            ActivityId = rule.ActivityId,
            StartTime = rule.StartTime,
            EndTime = rule.EndTime,
            IsBackground = rule.IsBackground,
            Location = rule.Location,
            Notes = rule.Notes,
            ImportanceId = rule.ImportanceId,
            Status = PlannerTaskStatus.NotStarted,
            SourceRepeatingTaskId = rule.Id
        };

    // ------------------------------------------------------------------ reconciling an edit

    private async Task ReconcileCoreAsync(RepeatingPlannerTask rule, RepeatingPlannerTask before, DateOnly today, CancellationToken ct)
    {
        var userId = rule.UserId;
        var stillPlaces = rule.AutoPlace && rule.IsActive;

        // 1. Untouched occurrences from today on follow the rule; on a day it no longer places, they go.
        var occurrences = await dbContext.Set<PlannerTask>()
            .IgnoreQueryFilters()
            .Include(t => t.Calendar)
            .Where(t => t.UserId == userId && t.SourceRepeatingTaskId == rule.Id && t.Calendar.Date >= today)
            .ToListAsync(ct);

        if (occurrences.Count > 0)
        {
            var ids = occurrences.Select(t => t.Id).ToList();
            var withReminder = await dbContext.Set<Reminder>()
                .IgnoreQueryFilters()
                .Where(r => r.PlannerTaskId != null && ids.Contains(r.PlannerTaskId.Value))
                .Select(r => r.PlannerTaskId!.Value)
                .ToHashSetAsync(ct);

            foreach (var occurrence in occurrences.Where(t => IsUntouched(t, before, withReminder)))
            {
                if (stillPlaces && RepeatingPlannerTaskSchedule.FallsOn(rule, occurrence.Calendar.Date, occurrence.Calendar.DayType))
                    CopyPlanOnto(rule, occurrence);
                else
                    dbContext.Set<PlannerTask>().Remove(occurrence);
            }
        }

        // 2. Already-decided days the edit newly matches. The watermark would otherwise skip them for good, and
        // "add Wednesday" on a Wednesday must put the block on today. Only while the rule stayed placing
        // throughout: if it was paused or not auto-placing, a day it "did not match" may be one whose occurrence
        // the user deleted before the pause, and that must not come back.
        if (before.AutoPlace && before.IsActive && stillPlaces && rule.MaterialisedThrough is { } through && through >= today)
        {
            var days = await LoadDaysAsync(userId, today, through, ct);
            var userAbsent = await IsUserAbsentAsync(userId, ct);
            var conflictResolution = await ConflictResolutionAsync(userId, ct);

            foreach (var calendar in days.Values.OrderBy(c => c.Date))
            {
                if (IsGated(calendar, userAbsent)
                    || RepeatingPlannerTaskSchedule.FallsOn(before, calendar.Date, calendar.DayType)
                    || !RepeatingPlannerTaskSchedule.FallsOn(rule, calendar.Date, calendar.DayType)
                    || HoldsOccurrenceOf(calendar, rule.Id))
                    continue;

                PlannerTemplateApplier.StageAgainstDay(dbContext, calendar, conflictResolution, [OccurrenceOf(rule, calendar)]);
            }
        }

        await SaveAndCancelOrphanedRemindersAsync(ct);
    }

    /// <summary>
    /// <b>Untouched</b>: still exactly what the rule placed — <c>NotStarted</c>, never slipped (no original slot),
    /// still on the rule's old span with its old activity, lane, location, notes and importance, and with nothing
    /// hung off it since (no to-do link, no Google event, no reminder). Anything the user did to it, a move inside
    /// the day included, makes it theirs, and an edit to the rule leaves it alone.
    /// <para>
    /// A block a destructive conflict mode carved on placement counts as touched too, since its span is no longer
    /// the rule's. That errs the safe way: a carved block is never re-stretched over the task it was carved around.
    /// </para>
    /// </summary>
    private static bool IsUntouched(PlannerTask task, RepeatingPlannerTask before, IReadOnlySet<long> withReminder) =>
        task.Status == PlannerTaskStatus.NotStarted
        && task.OriginalDate == null
        && task.TodolistItemId == null
        && task.GoogleEventId == null
        && !withReminder.Contains(task.Id)
        && task.StartTime == before.StartTime
        && task.EndTime == before.EndTime
        && task.ActivityId == before.ActivityId
        && task.IsBackground == before.IsBackground
        && task.Location == before.Location
        && task.Notes == before.Notes
        && task.ImportanceId == before.ImportanceId;

    /// <summary>
    /// An edited rule's new plan onto an untouched occurrence, in place. No conflict resolution: the block was
    /// already on the day, and an overlap the day view shows is recoverable where a silent carve is not.
    /// </summary>
    private static void CopyPlanOnto(RepeatingPlannerTask rule, PlannerTask occurrence)
    {
        occurrence.ActivityId = rule.ActivityId;
        occurrence.StartTime = rule.StartTime;
        occurrence.EndTime = rule.EndTime;
        occurrence.IsBackground = rule.IsBackground;
        occurrence.Location = rule.Location;
        occurrence.Notes = rule.Notes;
        occurrence.ImportanceId = rule.ImportanceId;
    }

    // ------------------------------------------------------------------ shared

    private async Task<DateOnly> TodayAsync(long userId, CancellationToken ct) =>
        TaskPlannerHelper.TodayIn(await timeZones.GetAsync(userId, ct));

    /// <summary>The user's days in the window, tracked and with their tasks — what the conflict modes need.</summary>
    private Task<Dictionary<DateOnly, Calendar>> LoadDaysAsync(long userId, DateOnly from, DateOnly until, CancellationToken ct) =>
        dbContext.Set<Calendar>()
            .IgnoreQueryFilters()
            .Include(c => c.Tasks)
            .Where(c => c.UserId == userId && c.Date >= from && c.Date <= until)
            .ToDictionaryAsync(c => c.Date, ct);

    private async Task<ApplyTemplateConflictResolutionEnum> ConflictResolutionAsync(long userId, CancellationToken ct) =>
        await dbContext.Set<UserPlannerSettings>()
            .IgnoreQueryFilters()
            .Where(s => s.UserId == userId)
            .Select(s => (ApplyTemplateConflictResolutionEnum?)s.DefaultConflictResolution)
            .FirstOrDefaultAsync(ct)
        ?? ConflictResolutionWithoutSettings;

    /// <summary>
    /// A destructive conflict mode deletes the user's own blocks, and their portal reminder rows go with them by
    /// FK cascade, which the Reminders module never sees — so the ids are read off the tracker before the save
    /// and cancelled after it, the same as a template apply.
    /// </summary>
    private async Task SaveAndCancelOrphanedRemindersAsync(CancellationToken ct)
    {
        if (!dbContext.ChangeTracker.HasChanges())
            return;

        var orphanedReminderIds = await reminders.GetReminderIdsForPlannerTasksAsync(
            PlannerTemplateApplier.StagedTaskDeletions(dbContext), ct);

        await dbContext.SaveChangesAsync(ct);

        if (orphanedReminderIds.Count > 0)
            await reminders.CancelManyAsync(orphanedReminderIds, ct);
    }

    /// <summary>
    /// The never-throws half of the contract. A lost race is expected and quiet; anything else is logged. Either
    /// way the tracker is cleared, because a failed save leaves its entities staged and the caller's own next save
    /// would re-send them.
    /// </summary>
    private async Task RunSafelyAsync(long userId, string what, Func<Task> work, CancellationToken ct)
    {
        try
        {
            await work();
        }
        catch (DbUpdateConcurrencyException)
        {
            dbContext.ChangeTracker.Clear();
            logger.LogDebug("Lost the race {What} repeating-task occurrences for user {UserId}; another request did it", what, userId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            dbContext.ChangeTracker.Clear();
            logger.LogWarning(ex, "Failed {What} repeating-task occurrences for user {UserId}; the next read retries", what, userId);
        }
    }
}
