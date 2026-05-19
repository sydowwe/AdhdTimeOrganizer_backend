using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.domain.model.entity.activity.lookup;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activity.lookup.expectedCostTier.command;

public class BatchDeleteActivityExpectedCostTierEndpoint(AppDbContext dbContext)
    : BaseBatchDeleteEndpoint<ActivityExpectedCostTier>(dbContext);
