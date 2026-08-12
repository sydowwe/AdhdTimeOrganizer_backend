using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.ActivityProfiles.application.endpoint.lookup.experienceType.command;

public class CreateActivityExperienceTypeEndpoint(DbContext dbContext)
    : BaseCreateLookupEndpoint<ActivityExperienceType>(dbContext);