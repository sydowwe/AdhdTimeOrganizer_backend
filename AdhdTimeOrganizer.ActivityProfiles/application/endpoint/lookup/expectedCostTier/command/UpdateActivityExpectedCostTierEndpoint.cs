using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.dto.request.generic;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.ActivityProfiles.application.endpoint.lookup.expectedCostTier.command;

public class UpdateActivityExpectedCostTierEndpoint(DbContext dbContext)
    : BaseUpdateEndpoint<ActivityExpectedCostTier, LookupRequest<ActivityExpectedCostTier>>(dbContext);