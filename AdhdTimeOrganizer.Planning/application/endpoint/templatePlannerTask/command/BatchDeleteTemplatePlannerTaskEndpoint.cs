using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.Planning.application.endpoint.activityPlanning.templatePlannerTask.command;

public class BatchDeleteTemplatePlannerTaskEndpoint(DbContext dbContext)
    : BaseBatchDeleteEndpoint<TemplatePlannerTask>(dbContext);