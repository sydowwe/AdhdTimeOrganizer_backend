using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.dto.request.generic;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.ActivityProfiles.application.endpoint.lookup.experienceType.command;

public class UpdateActivityExperienceTypeEndpoint(DbContext dbContext)
    : BaseUpdateEndpoint<ActivityExperienceType, LookupRequest<ActivityExperienceType>>(dbContext);