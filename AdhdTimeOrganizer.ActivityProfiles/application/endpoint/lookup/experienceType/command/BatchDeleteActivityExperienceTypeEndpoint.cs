using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.ActivityProfiles.application.endpoint.lookup.experienceType.command;

public class BatchDeleteActivityExperienceTypeEndpoint(DbContext dbContext)
    : BaseBatchDeleteEndpoint<ActivityExperienceType>(dbContext);