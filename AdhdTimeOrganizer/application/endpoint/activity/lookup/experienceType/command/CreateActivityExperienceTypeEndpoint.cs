using AdhdTimeOrganizer.domain.model.entity.activity.lookup;
using AdhdTimeOrganizer.infrastructure.persistence;
using Sydowwe.Framework.application.dto.request.generic;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.application.endpoint.activity.lookup.experienceType.command;

public class CreateActivityExperienceTypeEndpoint(AppDbContext dbContext)
    : BaseCreateEndpoint<ActivityExperienceType, LookupRequest<ActivityExperienceType>>(dbContext);