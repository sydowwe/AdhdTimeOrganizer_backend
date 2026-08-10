using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Planning.domain.serviceContract;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.Planning.application.endpoint.activityPlanning.plannerTask.command;

public class BatchDeletePlannerTaskEndpoint(DbContext dbContext, IReminderRegistrationService reminders)
    : BaseBatchDeleteEndpoint<PlannerTask>(dbContext)
{
    /// <summary>Same capture-before-cascade reasoning as the single delete.</summary>
    private IReadOnlyList<long> _attachedReminderIds = [];

    protected override async Task BeforeDeleteAsync(IReadOnlyList<PlannerTask> entities, CancellationToken ct = default) =>
        _attachedReminderIds = await reminders.GetReminderIdsForPlannerTasksAsync(entities.Select(e => e.Id).ToList(), ct);

    protected override Task AfterSave(IReadOnlyList<PlannerTask> entities, CancellationToken ct = default) => reminders.CancelManyAsync(_attachedReminderIds, ct);
}