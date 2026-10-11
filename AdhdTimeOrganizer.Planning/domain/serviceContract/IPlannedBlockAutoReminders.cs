namespace AdhdTimeOrganizer.Planning.domain.serviceContract;

/// <summary>
/// Audit N1 — a new planned block gets a reminder without the user asking, under their own
/// <c>UserPlannerSettings.AutoReminderMode</c>. Every path that <b>creates</b> a <c>PlannerTask</c> calls this once,
/// after its own save, with the ids it just created: the plain create, both template applies, the copy onto other
/// days, and the repeating-rule materialiser (which a scheduled sweep also drives). A move, a status change or a
/// re-sync never calls it — so a block gets its automatic reminder exactly once, and a user who deletes it wins.
/// <para>
/// What it creates is an ordinary portal <c>Reminder</c> row with <c>IsAuto = true</c>, registered through
/// <see cref="IReminderRegistrationService"/>, so everything that already keeps a task's reminders honest — every
/// move, status patch and delete path — covers it with no further wiring.
/// </para>
/// <para>
/// <b>Never throws.</b> The blocks have already committed and the caller has nothing left to roll back, so a
/// failure is logged (ids only) and the blocks simply have no automatic reminder.
/// </para>
/// </summary>
public interface IPlannedBlockAutoReminders
{
    /// <summary>
    /// Creates and registers the automatic reminder of each block in <paramref name="plannerTaskIds"/> that qualifies:
    /// foreground, <c>NotStarted</c>, starting in the future on its owner's clock, without a reminder yet, and
    /// matching its owner's mode. Ids that do not qualify, or no longer exist, are skipped. Works with no
    /// authenticated user — every row is written with the block's own <c>UserId</c>.
    /// </summary>
    Task CreateForNewBlocksAsync(IReadOnlyCollection<long> plannerTaskIds, CancellationToken ct = default);
}
