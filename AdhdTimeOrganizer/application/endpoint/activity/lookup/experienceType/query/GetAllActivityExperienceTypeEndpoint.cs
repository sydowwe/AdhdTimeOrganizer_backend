using AdhdTimeOrganizer.application.dto.response.generic;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.domain.model.entity.activity.lookup;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activity.lookup.experienceType.query;

public class GetAllActivityExperienceTypeEndpoint(AppDbContext dbContext)
    : BaseGetAllEndpoint<ActivityExperienceType, LookupResponse<ActivityExperienceType>>(dbContext);
