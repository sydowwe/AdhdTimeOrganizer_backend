using AdhdTimeOrganizer.Core.domain.serviceContract;
using AdhdTimeOrganizer.Planning.application.dto.request.taskPlanner.template;
using AdhdTimeOrganizer.Planning.application.endpoint.extensions;
using AdhdTimeOrganizer.Planning.application.validator;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.Planning.application.endpoint.activityPlanning.templatePlannerTask.command;

public class UpdateTemplatePlannerTaskEndpoint(DbContext dbContext, IReferencedIdOwnershipService ownership)
    : BaseUpdateEndpoint<TemplatePlannerTask, TemplatePlannerTaskRequest>(dbContext)
{
    private readonly DbContext _dbContext = dbContext;

    public override void Configure()
    {
        base.Configure();
        Validator<TemplatePlannerTaskValidator>();
    }

    /// <summary>
    /// Same three ids as the create path — and <c>UpdateEntity</c> writes <c>TemplateId</c> too, so an update
    /// can move one of the caller's own lines into a stranger's template.
    /// </summary>
    protected override async Task<bool> BeforeMapping(TemplatePlannerTaskRequest req, CancellationToken ct = default) =>
        await this.EnsureOwnsDayTemplateAsync(_dbContext, req.TemplateId, nameof(req.TemplateId), ct)
        && await this.EnsureOwnsBasePlannerTaskReferencesAsync(_dbContext, ownership, req, ct);
}
