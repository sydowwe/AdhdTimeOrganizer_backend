using AdhdTimeOrganizer.Core.domain.serviceContract;
using AdhdTimeOrganizer.Planning.application.dto.request.taskPlanner;
using AdhdTimeOrganizer.Planning.application.endpoint.extensions;
using AdhdTimeOrganizer.Planning.application.validator;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Planning.domain.serviceContract;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.Planning.application.endpoint.activityPlanning.repeatingPlannerTask.command;

public class UpdateRepeatingPlannerTaskEndpoint(
    DbContext dbContext,
    IReferencedIdOwnershipService ownership,
    IRepeatingTaskMaterialiser materialiser)
    : BaseUpdateEndpoint<RepeatingPlannerTask, RepeatingPlannerTaskRequest>(dbContext)
{
    private readonly DbContext _dbContext = dbContext;

    private RepeatingPlannerTask? _before;

    public override void Configure()
    {
        base.Configure();
        Validator<RepeatingPlannerTaskValidator>();
    }

    /// <summary>
    /// The update path needs the guard as much as the create one: the row being written is already the
    /// caller's, and it is the <c>ActivityId</c> being repointed at a stranger's row that the FK cannot catch.
    /// </summary>
    protected override Task<bool> BeforeMapping(RepeatingPlannerTaskRequest req, CancellationToken ct = default) =>
        this.EnsureOwnsBasePlannerTaskReferencesAsync(_dbContext, ownership, req, ct);

    /// <summary>
    /// The rule as it was, off the change tracker's original values, for FO6's reconcile: an occurrence is
    /// untouched only while it still matches what the <i>old</i> rule placed. Captured here because the base
    /// marks every property modified right after.
    /// </summary>
    protected override Task<bool> AfterMapping(RepeatingPlannerTask entity, RepeatingPlannerTaskRequest req, CancellationToken ct = default)
    {
        _before = (RepeatingPlannerTask)_dbContext.Entry(entity).OriginalValues.ToObject();
        return Task.FromResult(true);
    }

    /// <summary>
    /// FO6: untouched occurrences from today on follow the edit (or go, when the rule no longer places on their
    /// day), and a newly auto-placing rule lands on today and tomorrow straight away. Neither fails the update.
    /// </summary>
    protected override async Task AfterSave(RepeatingPlannerTask entity, CancellationToken ct = default)
    {
        await materialiser.ReconcileAfterEditAsync(entity, _before!, ct);
        if (entity.AutoPlace)
            await materialiser.PlaceDueAsync(entity.UserId, ct);
    }
}
