using AdhdTimeOrganizer.application.dto.response.generic;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.application.mapper;
using AdhdTimeOrganizer.domain.model.entity.activity.lookup;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activity.lookup.experienceType.query;

public class GetByIdActivityExperienceTypeEndpoint(AppDbContext dbContext, LookupMapper<ActivityExperienceType> mapper)
    : BaseGetByIdEndpoint<ActivityExperienceType, LookupResponse, LookupMapper<ActivityExperienceType>>(dbContext, mapper);
