using AdhdTimeOrganizer.Core.domain.model.entity.activity.lookup;
using Sydowwe.Framework.application.endpoint.@base.command;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.Core.application.endpoint.activity.lookup.expectedCostTier.command;

public class BatchDeleteActivityExpectedCostTierEndpoint(DbContext dbContext)
    : BaseBatchDeleteEndpoint<ActivityExpectedCostTier>(dbContext);