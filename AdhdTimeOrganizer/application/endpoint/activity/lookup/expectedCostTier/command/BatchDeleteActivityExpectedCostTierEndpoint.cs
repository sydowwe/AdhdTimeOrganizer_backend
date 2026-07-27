using AdhdTimeOrganizer.domain.model.entity.activity.lookup;
using AdhdTimeOrganizer.infrastructure.persistence;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.application.endpoint.activity.lookup.expectedCostTier.command;

public class BatchDeleteActivityExpectedCostTierEndpoint(AppDbContext dbContext)
    : BaseBatchDeleteEndpoint<ActivityExpectedCostTier>(dbContext);