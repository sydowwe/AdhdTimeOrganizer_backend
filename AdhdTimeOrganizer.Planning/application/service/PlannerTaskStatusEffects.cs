using AdhdTimeOrganizer.Core.application.@event;
using AdhdTimeOrganizer.Core.domain.serviceContract;
using AdhdTimeOrganizer.Planning.application.helper;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Planning.domain.serviceContract;
using FastEndpoints;
using Sydowwe.Framework.config.dependencyInjection;

namespace AdhdTimeOrganizer.Planning.application.service;

/// <inheritdoc cref="IPlannerTaskStatusEffects"/>
public class PlannerTaskStatusEffects(
    IReminderRegistrationService reminders,
    IUserTimeZoneResolver timeZones) : IPlannerTaskStatusEffects, IScopedService
{
    public async Task AfterStatusSavedAsync(PlannerTask task, CancellationToken ct = default)
    {
        // Finishing or calling off a task retires the reminder attached to it: a nudge about something
        // already dealt with is pure noise, and acting on the task is the user cancelling it implicitly.
        // Moving back to NotStarted/InProgress re-registers it — the same call covers both directions,
        // because the decision lives in the registration service, not here.
        await reminders.SyncForPlannerTasksAsync([task.Id], ct);

        // The user's today, not the server's — see TaskPlannerHelper.TodayIn. This decides whether the
        // event is published at all, so a UTC boundary would silently drop the fan-out for a task the
        // user completed after their own midnight.
        var today = TaskPlannerHelper.TodayIn(await timeZones.GetAsync(task.UserId, ct));
        if (task.Calendar.Date == today)
            await new PlannerTaskIsDoneChangedEvent(task.ActivityId, task.UserId, task.IsDone, task.TodolistItemId)
                .PublishAsync(Mode.WaitForAll, ct);
    }
}
