using AdhdTimeOrganizer.Core.domain.serviceContract;
using AdhdTimeOrganizer.Planning.application.dto.request.taskPlanner.template;
using AdhdTimeOrganizer.Planning.application.endpoint.extensions;
using AdhdTimeOrganizer.Planning.application.validator;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.Planning.application.endpoint.activityPlanning.templatePlannerTask.command;

public class CreateTemplatePlannerTaskEndpoint(DbContext dbContext, IReferencedIdOwnershipService ownership)
    : BaseCreateEndpoint<TemplatePlannerTask, TemplatePlannerTaskRequest>(dbContext)
{
    private readonly DbContext _dbContext = dbContext;

    public override void Configure()
    {
        base.Configure();
        Validator<TemplatePlannerTaskValidator>();
    }

    /// <summary>
    /// Three ids off the wire, none of them minted here. <c>ActivityId</c> / <c>ImportanceId</c> are the same
    /// pair every planner write carries; <c>TemplateId</c> is this request's own, and unchecked it files the
    /// line inside a stranger's day template — which then applies it to their day.
    /// </summary>
    protected override async Task<bool> BeforeMapping(TemplatePlannerTaskRequest req, CancellationToken ct = default) =>
        await this.EnsureOwnsDayTemplateAsync(_dbContext, req.TemplateId, nameof(req.TemplateId), ct)
        && await this.EnsureOwnsBasePlannerTaskReferencesAsync(_dbContext, ownership, req, ct);
}
