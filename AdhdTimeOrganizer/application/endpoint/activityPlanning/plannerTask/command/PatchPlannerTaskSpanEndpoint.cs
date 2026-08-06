using AdhdTimeOrganizer.application.dto.request.taskPlanner;
using AdhdTimeOrganizer.application.validator;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.domain.serviceContract;
using AdhdTimeOrganizer.infrastructure.persistence;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.plannerTask.command;

public class PatchPlannerTaskSpanEndpoint(AppDbContext dbContext, IReminderRegistrationService reminders)
    : BasePatchEndpoint<PlannerTask, PlannerTaskChangeSpanRequest>(dbContext)
{
    public override void Configure()
    {
        base.Configure();
        Validator<PlannerTaskChangeSpanValidator>();
    }

    protected override void Mapping(PlannerTask entity, PlannerTaskChangeSpanRequest req)
    {
        entity.StartTime = new TimeOnly(req.StartTime.Hours, req.StartTime.Minutes);
        entity.EndTime = new TimeOnly(req.EndTime.Hours, req.EndTime.Minutes);
    }

    /// <summary>
    /// The drag-the-task-around path, so the one that moves a reminder's instant most often. Re-registering is
    /// an upsert on the same key — no duplicate definition, and nothing left behind at the old time.
    /// </summary>
    protected override Task AfterSave(PlannerTask entity, CancellationToken ct = default) => reminders.SyncForPlannerTasksAsync([entity.Id], ct);
}