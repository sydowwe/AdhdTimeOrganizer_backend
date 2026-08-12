using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.endpoint.@base.read;

namespace AdhdTimeOrganizer.ActivityProfiles.application.endpoint.lookup.expectedCostTier.query;

public class GetAllActivityExpectedCostTierEndpoint(DbContext dbContext)
    : BaseGetAllLookupEndpoint<ActivityExpectedCostTier>(dbContext);