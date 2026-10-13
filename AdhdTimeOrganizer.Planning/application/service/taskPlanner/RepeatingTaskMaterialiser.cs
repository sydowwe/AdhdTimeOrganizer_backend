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
/// <item><b>Placed by lanes first</b> (<see cref="PlannerTemplateApplier.StageWithinLanes"/>). An occurrence that
/// leaves at most two blocks of its own kind (foreground, or separately background) at every minute goes on
/// whole, beside what is there, whatever the conflict mode — the SPA draws the two side by side. Occurrences
/// placed in the same run count against each other, the older rule (lower id) first.</item>
/// <item><b>Only a third block falls back to <c>UserPlannerSettings.DefaultConflictResolution</c>, and never
/// destructively.</b> <see cref="NonDestructive"/> turns <c>Overwrite</c> into <c>Ignore</c> and
/// <c>MergeOverwrite</c> into <c>MergeIgnore</c>: a template apply is a choice the user makes with the day in front
/// of them, but this runs from a read, and a GET must not delete or carve the blocks the user placed. So an
/// occurrence that would be a third is skipped, or cut down to the stretches where it would be a second, never
/// the other way.</item>
/// <item><b>Gates.</b> An away day (FO1: <c>Vacation</c>, <c>SickDay</c>) is passed over, as is every day while
/// <see cref="IsUserAbsentAsync"/> says the user is gone (which, from a user's own read, is never). A passed-over day is still
/// decided: marking the day as a workday again later does not bring the occurrence back. The suggestion panel
/// still offers the rule on that day. A day marked away <i>after</i> its occurrence was placed loses the
/// occurrence if it is still untouched (<see cref="WithdrawFromAwayDaysAsync"/>), and it does not come back
/// either.</item>
/// <item><b>Concurrency.</b> Home and the planner can read the same day at once. Both stage the same
/// occurrences, and both advance the same rule rows; the rule's <c>row_version</c> lets exactly one save through,
/// and the loser's whole save — occurrences included — rolls back.</item>
/// </list>
/// <para>
/// <b>Automatic reminders (audit N1).</b> Every block placed here is a new block, so after the save it goes to
/// <see cref="IPlannedBlockAutoReminders"/> like any other create. An automatic reminder is not something the user
/// hung off the occurrence, so it does not make it "touched" (<see cref="IsUntouched"/>) — only one they wrote does.
/// Because the materialiser also runs from <c>MaterialiseRepeatingTasksJobHandler</c> with no signed-in user, every
/// row it writes carries an explicit <c>UserId</c>; nothing here may rely on the ambient user to stamp one.
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
    IPlannedBlockAutoReminders autoReminders,
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

    public async Task WithdrawFromAwayDaysAsync(long userId, IReadOnlyCollection<DateOnly> dates, CancellationToken ct = default)
    {
        await RunSafelyAsync(userId, "withdrawing away-day",
            async () => await WithdrawCoreAsync(userId, dates, await TodayAsync(userId, ct), ct), ct);
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
        // Ordered, because placing by lanes is first come: when two rules and a user block meet, the older rule
        // takes the second lane and the newer one is the third.
        var rules = await DueRules(userId, lastDay).OrderBy(r => r.Id).ToListAsync(ct);
        var conflictResolution = await ConflictResolutionAsync(userId, ct);

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

            PlannerTemplateApplier.StageWithinLanes(dbContext, calendar, conflictResolution, occurrences);
        }

        var placed = await SaveAndCancelOrphanedRemindersAsync(ct);

        if (placed > 0)
            logger.LogInformation("Placed {Count} repeating-task block(s) for user {UserId} on {From}..{Until}",
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
    /// <b>FO5 looked, and this stays false.</b> FO5's check is <c>UserPresence.IsAbsent</c> over
    /// <c>User.LastActiveAt</c>, and every caller of this class is the user's own request: a day-plan read, or a
    /// rule's create or edit. The host stamps <c>LastActiveAt</c> before the endpoint runs, so the check would
    /// always answer "present" here. Worse, if it ever answered from a value read <i>before</i> the stamp, the
    /// first read after a holiday would gate today and tomorrow, and the watermark would make that permanent: the
    /// user would come back to a plan missing the very blocks the rule exists to put there.
    /// <para>
    /// The caller that is <b>not</b> the user now exists — <c>MaterialiseRepeatingTasksJobHandler</c> — and it still
    /// does not use this hook: it skips an absent user outright instead of calling in. Gating here would decide the
    /// days and pass the watermark over them, so the user would come back to a plan missing exactly the blocks the
    /// rule is for; skipping leaves the days undecided, and the user's first read after the return places them.
    /// </para>
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

        // 1. Untouched occurrences from today on follow the rule; on a day it no longer places, they go. The day's
        // other blocks come along, because a move that would make the occurrence a third block of its kind is
        // decided by the lanes, the same as a fresh placement (see MovesOutOfItsLanes).
        var occurrences = await dbContext.Set<PlannerTask>()
            .IgnoreQueryFilters()
            .Include(t => t.Calendar).ThenInclude(c => c.Tasks)
            .Where(t => t.UserId == userId && t.SourceRepeatingTaskId == rule.Id && t.Calendar.Date >= today)
            .ToListAsync(ct);

        ApplyTemplateConflictResolutionEnum? conflictResolution = null;
        if (occurrences.Count > 0)
        {
            var withReminder = await UserWrittenRemindersAsync(occurrences.Select(t => t.Id).ToList(), ct);

            foreach (var occurrence in occurrences.Where(t => IsUntouched(t, before, withReminder)))
            {
                if (!stillPlaces || !RepeatingPlannerTaskSchedule.FallsOn(rule, occurrence.Calendar.Date, occurrence.Calendar.DayType))
                    dbContext.Set<PlannerTask>().Remove(occurrence);
                else if (!MovesOutOfItsLanes(rule, occurrence))
                    CopyPlanOnto(rule, occurrence);
                else
                {
                    // Where the edited rule would now be a third block, its untouched occurrence is replaced by
                    // what placing the edited rule fresh would leave: nothing under Ignore, the stretches where it
                    // is still a second block under MergeIgnore. The user's blocks are not touched either way.
                    dbContext.Set<PlannerTask>().Remove(occurrence);
                    conflictResolution ??= await ConflictResolutionAsync(userId, ct);
                    PlannerTemplateApplier.StageWithinLanes(dbContext, occurrence.Calendar, conflictResolution.Value,
                        [OccurrenceOf(rule, occurrence.Calendar)]);
                }
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
            conflictResolution ??= await ConflictResolutionAsync(userId, ct);

            foreach (var calendar in days.Values.OrderBy(c => c.Date))
            {
                if (IsGated(calendar, userAbsent)
                    || RepeatingPlannerTaskSchedule.FallsOn(before, calendar.Date, calendar.DayType)
                    || !RepeatingPlannerTaskSchedule.FallsOn(rule, calendar.Date, calendar.DayType)
                    || HoldsOccurrenceOf(calendar, rule.Id))
                    continue;

                PlannerTemplateApplier.StageWithinLanes(dbContext, calendar, conflictResolution.Value, [OccurrenceOf(rule, calendar)]);
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
    /// An occurrence <c>MergeIgnore</c> carved on placement counts as touched too, since its span is no longer the
    /// rule's. That errs the safe way: a carved block is never re-stretched over the task it was carved around.
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
    /// Whether the edited <paramref name="rule"/> moves <paramref name="occurrence"/> to where it would be a third
    /// block of its kind at some minute <b>it newly covers</b> — the same rule as the single-task writes
    /// (<see cref="PlannerTaskLaneRule.NewlyCovered(ValueTuple{TimeOnly, TimeOnly}?, TimeOnly, TimeOnly)"/>): a
    /// change of kind judges the whole new span, otherwise only the new span minus the occurrence's current one.
    /// So an edit that keeps the span, shrinks it or moves it inside itself is never judged — a day that already
    /// holds three (from before the lanes, or a bulk path) must still let a rename or a new note reach the
    /// occurrence — and an extension into free time follows the rule even when the old span already clashes.
    /// </summary>
    private bool MovesOutOfItsLanes(RepeatingPlannerTask rule, PlannerTask occurrence)
    {
        var stretches = PlannerTaskLaneRule.NewlyCovered(
            rule.IsBackground == occurrence.IsBackground ? (occurrence.StartTime, occurrence.EndTime) : null,
            rule.StartTime, rule.EndTime);
        if (stretches.Count == 0)
            return false;

        var others = occurrence.Calendar.Tasks
            .Where(t => t != occurrence
                        && t.IsBackground == rule.IsBackground
                        && dbContext.Entry(t).State is not (EntityState.Deleted or EntityState.Detached))
            .Select(t => (t.StartTime, t.EndTime))
            .ToList();

        return PlannerTaskLaneRule.ExceedsLaneLimit(stretches, others);
    }

    /// <summary>
    /// An edited rule's new plan onto an untouched occurrence, in place, once <see cref="MovesOutOfItsLanes"/> has
    /// said it still fits beside the day's blocks. No carving of anything else: the block was already on the day.
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

    // ------------------------------------------------------------------ withdrawing from away days

    /// <summary>
    /// The away gate, applied late: an occurrence placed on today or tomorrow before the user marked that day as
    /// vacation or sick leave goes, if it is still exactly what the rule placed (<see cref="IsUntouched"/>, judged
    /// against the rule as it stands). One the user moved, started, linked or hung a reminder on is theirs and stays.
    /// <para>
    /// From today on only. A past day's blocks are what happened, or the record of what did not, and the
    /// materialiser never placed there anyway. The watermark is not touched: the day stays decided, so un-marking
    /// it later does not bring the block back — the same as a day that was away when the rule reached it.
    /// </para>
    /// </summary>
    private async Task WithdrawCoreAsync(long userId, IReadOnlyCollection<DateOnly> dates, DateOnly today, CancellationToken ct)
    {
        var upcoming = dates.Where(d => d >= today).Distinct().ToList();
        if (upcoming.Count == 0)
            return;

        var onAwayDays = (await dbContext.Set<PlannerTask>()
                .IgnoreQueryFilters()
                .Include(t => t.Calendar)
                .Where(t => t.UserId == userId && t.SourceRepeatingTaskId != null && upcoming.Contains(t.Calendar.Date))
                .ToListAsync(ct))
            .Where(t => t.Calendar.DayType.IsAway())
            .ToList();
        if (onAwayDays.Count == 0)
            return;

        var ruleIds = onAwayDays.Select(t => t.SourceRepeatingTaskId!.Value).Distinct().ToList();
        var rules = await dbContext.Set<RepeatingPlannerTask>()
            .IgnoreQueryFilters()
            .Where(r => r.UserId == userId && ruleIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, ct);
        var withReminder = await UserWrittenRemindersAsync(onAwayDays.Select(t => t.Id).ToList(), ct);

        var withdrawn = onAwayDays
            .Where(t => rules.TryGetValue(t.SourceRepeatingTaskId!.Value, out var rule) && IsUntouched(t, rule, withReminder))
            .ToList();
        if (withdrawn.Count == 0)
            return;

        dbContext.Set<PlannerTask>().RemoveRange(withdrawn);
        await SaveAndCancelOrphanedRemindersAsync(ct);

        logger.LogInformation("Withdrew {Count} repeating-task occurrence(s) from away days for user {UserId}",
            withdrawn.Count, userId);
    }

    // ------------------------------------------------------------------ shared

    /// <summary>
    /// The occurrences among <paramref name="taskIds"/> carrying a reminder the user wrote. An automatic one
    /// (<c>IsAuto</c>) does not count: it is not something the user hung off the block.
    /// </summary>
    private async Task<HashSet<long>> UserWrittenRemindersAsync(List<long> taskIds, CancellationToken ct) =>
        await dbContext.Set<Reminder>()
            .IgnoreQueryFilters()
            .Where(r => r.PlannerTaskId != null && taskIds.Contains(r.PlannerTaskId.Value) && !r.IsAuto)
            .Select(r => r.PlannerTaskId!.Value)
            .ToHashSetAsync(ct);

    /// <summary>
    /// The user's conflict mode, made safe for a write nobody asked for at that moment: the two modes that delete
    /// or carve the user's existing blocks become their twins that only skip or carve the occurrence.
    /// </summary>
    private static ApplyTemplateConflictResolutionEnum NonDestructive(ApplyTemplateConflictResolutionEnum mode) => mode switch
    {
        ApplyTemplateConflictResolutionEnum.Overwrite => ApplyTemplateConflictResolutionEnum.Ignore,
        ApplyTemplateConflictResolutionEnum.MergeOverwrite => ApplyTemplateConflictResolutionEnum.MergeIgnore,
        _ => mode
    };

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
        NonDestructive(await dbContext.Set<UserPlannerSettings>()
                           .IgnoreQueryFilters()
                           .Where(s => s.UserId == userId)
                           .Select(s => (ApplyTemplateConflictResolutionEnum?)s.DefaultConflictResolution)
                           .FirstOrDefaultAsync(ct)
                       ?? ConflictResolutionWithoutSettings);

    /// <summary>
    /// Every deletion here is of an occurrence — removed by a rule edit, or withdrawn from a day marked away — and
    /// its portal reminder rows go with it by FK cascade, which the Reminders module never sees. So the ids are read
    /// off the tracker before the save and cancelled after it, the same as a template apply.
    /// <para>
    /// After the save the other two reminder duties run, both against the tracker as it stood before it: the blocks
    /// placed (occurrences and any carved segments) get their automatic reminders (audit N1), and the occurrences a
    /// rule edit moved in place re-sync theirs, which would otherwise keep firing at the old start.
    /// </para>
    /// Answers how many blocks were placed (occurrences and segments).
    /// </summary>
    private async Task<int> SaveAndCancelOrphanedRemindersAsync(CancellationToken ct)
    {
        if (!dbContext.ChangeTracker.HasChanges())
            return 0;

        var orphanedReminderIds = await reminders.GetReminderIdsForPlannerTasksAsync(
            PlannerTemplateApplier.StagedTaskDeletions(dbContext), ct);
        var placed = PlannerTemplateApplier.StagedTaskAdditions(dbContext);
        var moved = dbContext.ChangeTracker.Entries<PlannerTask>()
            .Where(e => e.State == EntityState.Modified)
            .Select(e => e.Entity.Id)
            .ToList();

        await dbContext.SaveChangesAsync(ct);

        if (orphanedReminderIds.Count > 0)
            await reminders.CancelManyAsync(orphanedReminderIds, ct);
        if (moved.Count > 0)
            await reminders.SyncForPlannerTasksAsync(moved, ct);
        if (placed.Count > 0)
            await autoReminders.CreateForNewBlocksAsync(placed.Select(t => t.Id).ToList(), ct);

        return placed.Count;
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
