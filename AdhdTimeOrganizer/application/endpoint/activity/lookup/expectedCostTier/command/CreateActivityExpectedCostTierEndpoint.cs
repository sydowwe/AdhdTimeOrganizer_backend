using AdhdTimeOrganizer.domain.model.entity.activity.lookup;
using AdhdTimeOrganizer.infrastructure.persistence;
using Sydowwe.Framework.application.dto.request.generic;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.application.endpoint.activity.lookup.expectedCostTier.command;

public class CreateActivityExpectedCostTierEndpoint(AppDbContext dbContext)
    : BaseCreateLookupEndpoint<ActivityExpectedCostTier>(dbContext);