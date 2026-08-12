using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.endpoint.@base.read;

namespace AdhdTimeOrganizer.ActivityProfiles.application.endpoint.lookup.locationType.query;

public class GetAllActivityLocationTypeEndpoint(DbContext dbContext)
    : BaseGetAllLookupEndpoint<ActivityLocationType>(dbContext);