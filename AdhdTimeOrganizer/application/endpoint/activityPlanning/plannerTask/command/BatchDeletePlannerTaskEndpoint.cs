using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.domain.serviceContract;
using AdhdTimeOrganizer.infrastructure.persistence;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.plannerTask.command;

public class BatchDeletePlannerTaskEndpoint(AppDbContext dbContext, IReminderRegistrationService reminders)
    : BaseBatchDeleteEndpoint<PlannerTask>(dbContext)
{
    /// <summary>Same capture-before-cascade reasoning as the single delete.</summary>
    private IReadOnlyList<long> _attachedReminderIds = [];

    protected override async Task BeforeDeleteAsync(IReadOnlyList<PlannerTask> entities, CancellationToken ct = default) =>
        _attachedReminderIds = await reminders.GetReminderIdsForPlannerTasksAsync(entities.Select(e => e.Id).ToList(), ct);

    protected override Task AfterSave(IReadOnlyList<PlannerTask> entities, CancellationToken ct = default) => reminders.CancelManyAsync(_attachedReminderIds, ct);
}