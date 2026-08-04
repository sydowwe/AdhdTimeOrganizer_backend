using AdhdTimeOrganizer.application.dto.request.taskPlanner;
using AdhdTimeOrganizer.application.validator;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.domain.serviceContract;
using AdhdTimeOrganizer.infrastructure.persistence;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.plannerTask.command;

public class UpdatePlannerTaskEndpoint(AppDbContext dbContext, IReminderRegistrationService reminders)
    : BaseUpdateEndpoint<PlannerTask, PlannerTaskRequest>(dbContext)
{
    public override void Configure()
    {
        base.Configure();
        Validator<PlannerTaskValidator>();
    }

    /// <summary>
    /// A task-linked reminder's instant is derived from the task's day + start time, so any full update has to
    /// re-register — an update can move the time, move the day (a different calendar) or change the status.
    /// A task with no reminder attached resolves to an empty set and costs one indexed lookup.
    /// </summary>
    protected override Task AfterSave(PlannerTask entity, CancellationToken ct = default) => reminders.SyncForPlannerTasksAsync([entity.Id], ct);
}
