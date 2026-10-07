using AdhdTimeOrganizer.Core.domain.serviceContract;
using AdhdTimeOrganizer.Planning.application.dto.request.taskPlanner;
using AdhdTimeOrganizer.Planning.application.endpoint.extensions;
using AdhdTimeOrganizer.Planning.application.validator;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Planning.domain.serviceContract;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.Planning.application.endpoint.activityPlanning.repeatingPlannerTask.command;

public class CreateRepeatingPlannerTaskEndpoint(
    DbContext dbContext,
    IReferencedIdOwnershipService ownership,
    IRepeatingTaskMaterialiser materialiser)
    : BaseCreateEndpoint<RepeatingPlannerTask, RepeatingPlannerTaskRequest>(dbContext)
{
    private readonly DbContext _dbContext = dbContext;

    public override void Configure()
    {
        base.Configure();
        Validator<RepeatingPlannerTaskValidator>();
    }

    /// <summary>
    /// <c>ActivityId</c> and <c>ImportanceId</c> come off the wire and the FK only proves the row exists — see
    /// <c>PlannerReferencedIdOwnershipExtensions</c>, which the ordinary planner-task writes call for the same
    /// two ids.
    /// </summary>
    protected override Task<bool> BeforeMapping(RepeatingPlannerTaskRequest req, CancellationToken ct = default) =>
        this.EnsureOwnsBasePlannerTaskReferencesAsync(_dbContext, ownership, req, ct);

    /// <summary>
    /// An auto-placing rule lands on today's and tomorrow's plan straight away (FO6), not on the next home load —
    /// the day planner reads through the filter endpoint and never triggers the day-plan read's hook. Never fails
    /// the create: the rule is already saved, and the next read retries.
    /// </summary>
    protected override Task AfterSave(RepeatingPlannerTask entity, CancellationToken ct = default) =>
        entity.AutoPlace ? materialiser.PlaceDueAsync(entity.UserId, ct) : Task.CompletedTask;
}
