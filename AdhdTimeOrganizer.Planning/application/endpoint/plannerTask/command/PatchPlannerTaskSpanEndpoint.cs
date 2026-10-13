using AdhdTimeOrganizer.Core.domain.serviceContract;
using AdhdTimeOrganizer.Planning.application.dto.request.taskPlanner;
using AdhdTimeOrganizer.Planning.application.endpoint.extensions;
using AdhdTimeOrganizer.Planning.application.helper;
using AdhdTimeOrganizer.Planning.application.validator;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Planning.domain.serviceContract;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.Planning.application.endpoint.activityPlanning.plannerTask.command;

public class PatchPlannerTaskSpanEndpoint(
    DbContext dbContext,
    IReminderRegistrationService reminders,
    IUserTimeZoneResolver timeZones)
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
    /// The grid's drag, resize and arrow moves: refused when they would make a third overlapping block
    /// (<see cref="PlannerTaskLaneRule"/>), the same check the grid makes first. Dragging an overdue block later in
    /// its day is a postponement too (FO3).
    /// </summary>
    protected override async Task<bool> AfterMapping(PlannerTask entity, PlannerTaskChangeSpanRequest req, CancellationToken ct = default)
    {
        if (!await this.EnsureWithinLanesAsync(DbContext, entity, ct))
            return false;

        await PlannerTaskMoveRecorder.RecordPostponementAsync(DbContext, entity, timeZones, ct);
        return true;
    }

    /// <summary>
    /// The drag-the-task-around path, so the one that moves a reminder's instant most often. Re-registering is
    /// an upsert on the same key — no duplicate definition, and nothing left behind at the old time.
    /// </summary>
    protected override Task AfterSave(PlannerTask entity, CancellationToken ct = default) => reminders.SyncForPlannerTasksAsync([entity.Id], ct);
}