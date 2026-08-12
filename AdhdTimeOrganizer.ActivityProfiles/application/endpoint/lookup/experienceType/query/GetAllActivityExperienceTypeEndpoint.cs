using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.endpoint.@base.read;

namespace AdhdTimeOrganizer.ActivityProfiles.application.endpoint.lookup.experienceType.query;

public class GetAllActivityExperienceTypeEndpoint(DbContext dbContext)
    : BaseGetAllLookupEndpoint<ActivityExperienceType>(dbContext);