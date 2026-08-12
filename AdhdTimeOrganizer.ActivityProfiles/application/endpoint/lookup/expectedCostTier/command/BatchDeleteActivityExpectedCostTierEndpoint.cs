using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.ActivityProfiles.application.endpoint.lookup.expectedCostTier.command;

public class BatchDeleteActivityExpectedCostTierEndpoint(DbContext dbContext)
    : BaseBatchDeleteEndpoint<ActivityExpectedCostTier>(dbContext);