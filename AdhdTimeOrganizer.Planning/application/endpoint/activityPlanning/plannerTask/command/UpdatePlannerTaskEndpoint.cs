using AdhdTimeOrganizer.Planning.application.dto.request.taskPlanner;
using AdhdTimeOrganizer.Planning.application.validator;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Planning.domain.serviceContract;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.Planning.application.endpoint.activityPlanning.plannerTask.command;

public class UpdatePlannerTaskEndpoint(DbContext dbContext, IReminderRegistrationService reminders)
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