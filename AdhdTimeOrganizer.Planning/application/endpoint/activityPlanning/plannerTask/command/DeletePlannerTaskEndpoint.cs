using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Planning.domain.serviceContract;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.Planning.application.endpoint.activityPlanning.plannerTask.command;

public class DeletePlannerTaskEndpoint(DbContext dbContext, IReminderRegistrationService reminders)
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