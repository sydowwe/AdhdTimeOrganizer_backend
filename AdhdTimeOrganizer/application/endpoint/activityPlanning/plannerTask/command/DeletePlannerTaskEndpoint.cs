using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.domain.serviceContract;
using AdhdTimeOrganizer.infrastructure.persistence;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.plannerTask.command;

public class DeletePlannerTaskEndpoint(AppDbContext dbContext, IReminderRegistrationService reminders)
    : BaseDeleteEndpoint<PlannerTask>(dbContext)
{
    /// <summary>
    /// Captured before the delete: the reminder rows go with the task by FK cascade, so after the save there
    /// is nothing left to look up — and the module would go on firing a reminder for a task that is gone.
    /// </summary>
    private IReadOnlyList<long> _attachedReminderIds = [];

    protected override async Task BeforeDeleteAsync(PlannerTask entity, CancellationToken ct = default) =>
        _attachedReminderIds = await reminders.GetReminderIdsForPlannerTasksAsync([entity.Id], ct);

    protected override Task AfterSave(PlannerTask entity, CancellationToken ct = default) => reminders.CancelManyAsync(_attachedReminderIds, ct);
}