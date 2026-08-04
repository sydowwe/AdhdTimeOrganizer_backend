using AdhdTimeOrganizer.domain.model.entity.reminder;

namespace AdhdTimeOrganizer.domain.serviceContract;

/// <summary>
/// The one seam between the portal's <see cref="Reminder"/> rows and the Reminders module's scheduling
/// registry. Every call site — the reminder CRUD endpoints and the planner-task write paths — goes through
/// here so the key format, the schedule mapping and the cancel-vs-register decision exist exactly once.
/// <para>
/// <b>Always call after the portal row has been saved.</b> The registry writes through the same scoped
/// <c>DbContext</c> and commits, so registering before the owning row is committed would publish a schedule
/// for a row that may never exist.
/// </para>
/// </summary>
public interface IReminderRegistrationService
{
    /// <summary>
    /// Bring the module's schedule in line with one portal reminder: register it, or cancel it when the
    /// linked planner task has been finished off. Idempotent — the registry upserts by key, so calling this
    /// after every edit is the whole re-registration story.
    /// </summary>
    Task SyncAsync(Reminder reminder, CancellationToken ct = default);

    /// <summary>
    /// Fill in the lead offsets the user didn't specify. Applies only when the request omitted them entirely
    /// and the reminder tracks a planner task: that is where <c>UserPlannerSettings.ReminderMinutesBefore</c>
    /// means something ("nudge me N minutes before a task starts").
    /// <para>
    /// <c>UserPlannerSettings.RemindersEnabled = false</c> suppresses the <i>prefill</i>, never the reminder —
    /// a reminder the user explicitly created always fires, at the moment itself if there is no lead default
    /// to apply. That setting is a preference about being offered reminders, not a kill switch over them.
    /// </para>
    /// </summary>
    Task ApplyUserDefaultsAsync(Reminder reminder, bool leadOffsetsWereSpecified, CancellationToken ct = default);

    /// <summary>Withdraw one reminder from the module. Idempotent, and a no-op for an unknown id.</summary>
    Task CancelAsync(long reminderId, CancellationToken ct = default);

    /// <summary>Withdraw several reminders at once. Idempotent.</summary>
    Task CancelManyAsync(IReadOnlyCollection<long> reminderIds, CancellationToken ct = default);

    /// <summary>
    /// Re-sync every reminder attached to these planner tasks — call it whenever a task's start time moves or
    /// its status changes, since a task-linked reminder's instant is derived from the task.
    /// </summary>
    Task SyncForPlannerTasksAsync(IReadOnlyCollection<long> plannerTaskIds, CancellationToken ct = default);

    /// <summary>
    /// The reminder ids attached to these planner tasks. Needed <b>before</b> a task delete: the portal rows
    /// go with the task by FK cascade, which the module never sees, so the ids have to be captured while they
    /// still exist and handed to <see cref="CancelManyAsync"/> once the delete commits.
    /// </summary>
    Task<IReadOnlyList<long>> GetReminderIdsForPlannerTasksAsync(IReadOnlyCollection<long> plannerTaskIds, CancellationToken ct = default);
}
