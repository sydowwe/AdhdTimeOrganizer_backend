using AdhdTimeOrganizer.application.dto.response.generic;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.application.mapper;
using AdhdTimeOrganizer.domain.model.entity.activity.lookup;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activity.lookup.expectedCostTier.query;

public class GetByIdActivityExpectedCostTierEndpoint(AppDbContext dbContext, LookupMapper<ActivityExpectedCostTier> mapper)
    : BaseGetByIdEndpoint<ActivityExpectedCostTier, LookupResponse, LookupMapper<ActivityExpectedCostTier>>(dbContext, mapper);
