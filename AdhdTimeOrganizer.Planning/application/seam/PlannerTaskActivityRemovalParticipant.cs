using AdhdTimeOrganizer.Core.application.seam;
using AdhdTimeOrganizer.Planning.domain.model.entity.reminder;
using AdhdTimeOrganizer.Planning.domain.serviceContract;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.config.dependencyInjection;

namespace AdhdTimeOrganizer.Planning.application.seam;

/// <summary>
/// Withdraws the registrations of personal reminders that an activity removal takes with it. Deleting an activity
/// cascades its planner tasks, and each task cascades the <see cref="Reminder"/> rows attached to it
/// (<c>ReminderConfiguration</c>) — so the rows go, but their <c>ReminderDefinition</c>s stay registered, sit on the
/// user's upcoming list and fire for a task that no longer exists.
/// <para>
/// Before the removal it reads the ids of the reminders attached to tasks on those activities; after the commit it
/// cancels whichever of them are gone. Checking rather than cancelling all of them is what makes it right for a
/// merge too: there the tasks are repointed onto the survivor, keep their reminders, and nothing is cancelled.
/// </para>
/// </summary>
public sealed class PlannerTaskActivityRemovalParticipant(DbContext dbContext, IReminderRegistrationService reminders)
    : IActivityRemovalParticipant, IScopedService
{
    public string Key => ActivityRemovalParticipantKeys.PlannerTask;

    public async Task<Func<CancellationToken, Task>?> BeforeRemovalAsync(
        DbContext db, IReadOnlyCollection<long> activityIds, CancellationToken ct)
    {
        // Under the caller's own filters: the activities are the caller's, so are their tasks' reminders.
        var reminderIds = await db.Set<Reminder>()
            .AsNoTracking()
            .Where(r => r.PlannerTaskId != null && activityIds.Contains(r.PlannerTask!.ActivityId))
            .Select(r => r.Id)
            .ToListAsync(ct);

        if (reminderIds.Count == 0)
            return null;

        return async token =>
        {
            var surviving = await dbContext.Set<Reminder>()
                .AsNoTracking()
                .Where(r => reminderIds.Contains(r.Id))
                .Select(r => r.Id)
                .ToListAsync(token);

            await reminders.CancelManyAsync(reminderIds.Except(surviving).ToList(), token);
        };
    }
}
