using AdhdTimeOrganizer.application.dto.request.generic;
using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.application.mapper;
using AdhdTimeOrganizer.domain.model.entity.activity.lookup;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activity.lookup.expectedCostTier.command;

public class UpdateActivityExpectedCostTierEndpoint(AppDbContext dbContext, LookupMapper<ActivityExpectedCostTier> mapper)
    : BaseUpdateEndpoint<ActivityExpectedCostTier, SelectOptionRequest, LookupMapper<ActivityExpectedCostTier>>(dbContext, mapper);
