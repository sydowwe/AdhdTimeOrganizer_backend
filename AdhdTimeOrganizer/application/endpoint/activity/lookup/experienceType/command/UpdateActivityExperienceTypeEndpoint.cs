using AdhdTimeOrganizer.domain.model.entity.activity.lookup;
using AdhdTimeOrganizer.infrastructure.persistence;
using Sydowwe.Framework.application.dto.request.generic;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.application.endpoint.activity.lookup.experienceType.command;

public class UpdateActivityExperienceTypeEndpoint(AppDbContext dbContext)
    : BaseUpdateEndpoint<ActivityExperienceType, LookupRequest<ActivityExperienceType>>(dbContext);