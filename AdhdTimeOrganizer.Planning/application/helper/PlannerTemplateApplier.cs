using AdhdTimeOrganizer.Planning.application.dto.@enum;
using AdhdTimeOrganizer.Planning.application.dto.request.taskPlanner;
using AdhdTimeOrganizer.Planning.domain.model.entity;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Planning.domain.model.@enum;

namespace AdhdTimeOrganizer.Planning.application.helper;

/// <summary>
/// Applying a day template to one calendar day: the template stamp, the four conflict-resolution modes and
/// the carving they share. Lives here rather than on <c>ApplyTemplatePlannerTaskEndpoint</c> because
/// <c>ApplyTemplateBatchPlannerTaskEndpoint</c> applies the same template to N days and must resolve
/// conflicts identically on each — a second copy of the carving would drift the moment one of the four modes
/// is touched, and nothing would fail to compile when it did.
/// <para>
/// Everything here only <b>stages</b>: it adds and removes on the passed <c>DbContext</c> and never saves.
/// The caller owns the save, and must read the deletions off the ChangeTracker <i>before</i> it — that is
/// where the reminder ids of cascaded-away tasks are captured.
/// </para>
/// </summary>
public static class PlannerTemplateApplier
{
    /// <summary>
    /// Turns the request's tasks into entities for one day. The resolved <paramref name="calendarId"/> wins
    /// over whatever each task carried: the client used to repeat the id per task, which could only ever
    /// agree with the request's own or be a bug.
    /// </summary>
    public static List<PlannerTask> BuildTasks(IEnumerable<PlannerTaskRequest> tasksFromTemplate, long userId, long calendarId) =>
        tasksFromTemplate.Select(t =>
        {
            var entity = t.ToEntity;
            entity.UserId = userId;
            entity.CalendarId = calendarId;
            return entity;
        }).ToList();

    /// <summary>
    /// Stamps the template onto the day, resolves the conflicts between <paramref name="newTasks"/> and the
    /// day's existing tasks, and stages the resulting adds/removes. <paramref name="calendar"/> must be loaded
    /// with its <c>Tasks</c>.
    /// </summary>
    public static void Stage(
        DbContext dbContext,
        Calendar calendar,
        TaskPlannerDayTemplate template,
        ApplyTemplateConflictResolutionEnum conflictResolution,
        List<PlannerTask> newTasks)
    {
        StampTemplate(template, calendar);

        var existingTasks = calendar.Tasks.ToList();

        switch (conflictResolution)
        {
            case ApplyTemplateConflictResolutionEnum.Ignore:
                newTasks = newTasks.Where(nt => !existingTasks.Any(et =>
                    et.TasksOverlap(nt.StartTime, nt.EndTime))).ToList();
                break;

            case ApplyTemplateConflictResolutionEnum.Overwrite:
                var conflictingExisting = existingTasks.Where(et =>
                    newTasks.Any(nt => et.TasksOverlap(nt.StartTime, nt.EndTime))).ToList();
                dbContext.Set<PlannerTask>().RemoveRange(conflictingExisting);
                break;

            case ApplyTemplateConflictResolutionEnum.MergeIgnore:
                newTasks = MergeCarveNewTasksAroundExisting(newTasks, existingTasks);
                break;

            case ApplyTemplateConflictResolutionEnum.MergeOverwrite:
                MergeCarveExistingTasksAroundNew(dbContext, existingTasks, newTasks);
                break;
        }

        dbContext.Set<PlannerTask>().AddRange(newTasks);
    }

    /// <summary>
    /// The ids of the planner tasks the staged resolution is about to delete — read this <b>before</b>
    /// saving, so their reminders can be cancelled once the delete commits.
    /// <para>
    /// Every conflict resolution except <c>Ignore</c> removes existing tasks (<c>MergeOverwrite</c> removes
    /// and re-adds carved segments), and each removal cascades that task's reminders away. Reading the
    /// ChangeTracker instead of threading a list through the four branches keeps this correct even if a new
    /// resolution mode is added later — whatever it deletes is seen here.
    /// </para>
    /// </summary>
    public static List<long> StagedTaskDeletions(DbContext dbContext) =>
        dbContext.ChangeTracker.Entries<PlannerTask>()
            .Where(e => e.State == EntityState.Deleted)
            .Select(e => e.Entity.Id)
            .ToList();

    private static void StampTemplate(TaskPlannerDayTemplate template, Calendar calendar)
    {
        calendar.AppliedTemplateId = template.Id;
        calendar.AppliedTemplateName = template.Name;
        calendar.WakeUpTime = template.DefaultWakeUpTime ?? calendar.WakeUpTime;
        calendar.BedTime = template.DefaultBedTime ?? calendar.BedTime;

        template.LastUsedAt = DateTime.UtcNow;
        template.UsageCount++;
    }

    /// <summary>
    /// MergeIgnore: Carve new tasks around existing ones.
    /// If an existing task is in the middle of a new task, split the new task.
    /// If a new task overlaps with existing, shorten it to fit.
    /// </summary>
    private static List<PlannerTask> MergeCarveNewTasksAroundExisting(List<PlannerTask> newTasks, List<PlannerTask> existingTasks)
    {
        var result = new List<PlannerTask>();

        foreach (var segments in newTasks.Select(newTask => CarveTaskAroundBlockers(newTask, existingTasks)))
            result.AddRange(segments);

        return result;
    }

    /// <summary>
    /// MergeOverwrite: Carve existing tasks around new ones.
    /// If a new task is in the middle of an existing task, split the existing task.
    /// If an existing task overlaps with new, shorten it to fit.
    /// </summary>
    private static void MergeCarveExistingTasksAroundNew(DbContext dbContext, List<PlannerTask> existingTasks, List<PlannerTask> newTasks)
    {
        var tasksToRemove = new List<PlannerTask>();
        var tasksToAdd = new List<PlannerTask>();

        foreach (var existingTask in existingTasks)
        {
            var overlappingNewTasks = newTasks.Where(nt =>
                existingTask.TasksOverlap(nt.StartTime, nt.EndTime)).ToList();

            if (overlappingNewTasks.Count == 0)
                continue;

            // A task that is not carvable is left whole rather than staged for removal. Carving one returns no
            // segments at all (see IsCarvable), so the old order — remove first, then carve — deleted the task,
            // put nothing back, cascaded its reminders away through StagedTaskDeletions, and answered 201. The
            // new task is added over it instead: an overlap the day view can show is recoverable, a silent
            // delete is not.
            if (!IsCarvable(existingTask))
                continue;

            tasksToRemove.Add(existingTask);
            var segments = CarveTaskAroundBlockers(existingTask, overlappingNewTasks);
            tasksToAdd.AddRange(segments);
        }

        dbContext.Set<PlannerTask>().RemoveRange(tasksToRemove);
        dbContext.Set<PlannerTask>().AddRange(tasksToAdd);
    }

    /// <summary>
    /// A span the carving walk can reason about: strictly forward within one day. Everything below compares
    /// <c>currentStart</c> against <c>task.EndTime</c>, so a row with <c>EndTime &lt;= StartTime</c> — which
    /// <c>PlannerTaskValidator</c> accepted until the ordering rule was added, and which older rows still carry
    /// — makes every comparison read on the wrong side of midnight and yields an empty segment list.
    /// <para>
    /// Callers must consult this <b>before</b> staging the original for removal; an empty list from a carvable
    /// task means "fully covered by blockers", from a non-carvable one it means "the arithmetic gave up".
    /// </para>
    /// </summary>
    private static bool IsCarvable(PlannerTask task) => task.StartTime < task.EndTime;

    /// <summary>
    /// Carves a task around blocking tasks, returning segments that don't overlap with blockers.
    /// Handles splitting when a blocker is in the middle, and shortening when blockers overlap edges.
    /// A task that is not <see cref="IsCarvable"/> comes back whole — see that method.
    /// </summary>
    private static List<PlannerTask> CarveTaskAroundBlockers(PlannerTask task, List<PlannerTask> blockers)
    {
        if (!IsCarvable(task))
            return [task];

        var overlappingBlockers = blockers
            .Where(b => task.TasksOverlap(b.StartTime, b.EndTime))
            .OrderBy(b => b.StartTime)
            .ToList();

        if (overlappingBlockers.Count == 0)
            return [task];

        var result = new List<PlannerTask>();
        var currentStart = task.StartTime;

        foreach (var blocker in overlappingBlockers)
        {
            if (currentStart < blocker.StartTime)
            {
                var segmentEnd = blocker.StartTime < task.EndTime ? blocker.StartTime : task.EndTime;
                if (currentStart < segmentEnd)
                    result.Add(CloneTaskWithNewTimes(task, currentStart, segmentEnd));
            }

            currentStart = blocker.EndTime > currentStart ? blocker.EndTime : currentStart;
        }

        if (currentStart < task.EndTime)
            result.Add(CloneTaskWithNewTimes(task, currentStart, task.EndTime));

        return result;
    }

    /// <summary>
    /// A carved segment is a <b>new block</b>, not a continuation of the task it came from — the same reading
    /// <c>CopyToDaysPlannerTaskEndpoint.CopyOnto</c> takes, and for the same two reasons.
    /// <para>
    /// <c>Status</c> resets to <c>NotStarted</c> because the actual start/end times, skip reason and Google
    /// event id do not come across: carrying <c>Completed</c> without them produces a task that claims to be
    /// finished at times it was never worked, which <c>PlanVsActualTrendService</c> then counts as completed
    /// but unmeasurable. And <c>TodolistItemId</c> is dropped because a to-do item pairs with the one planner
    /// task it was planned as — copying the id onto both halves of a split would point two tasks at one item
    /// and fan every done-toggle across both.
    /// </para>
    /// <para>
    /// <c>SourceTemplateTaskId</c> <i>is</i> carried: it records which template line the block came from, which
    /// is still true of every segment.
    /// </para>
    /// <para>
    /// This applies to both merge modes, so a <i>new</i> task that <c>MergeIgnore</c> splits also comes out
    /// <c>NotStarted</c> even if the request named another status. That is the same ruling, not an oversight:
    /// the segment is a different span from the one the caller described, and a status that outlived the span
    /// it was about is what the reset exists to prevent.
    /// </para>
    /// </summary>
    private static PlannerTask CloneTaskWithNewTimes(PlannerTask original, TimeOnly newStart, TimeOnly newEnd) =>
        new()
        {
            StartTime = newStart,
            EndTime = newEnd,
            IsBackground = original.IsBackground,
            Location = original.Location,
            Notes = original.Notes,
            ImportanceId = original.ImportanceId,
            ActivityId = original.ActivityId,
            CalendarId = original.CalendarId,
            Status = PlannerTaskStatus.NotStarted,
            SourceTemplateTaskId = original.SourceTemplateTaskId,
            UserId = original.UserId
        };
}
