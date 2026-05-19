using AdhdTimeOrganizer.application.dto.request.generic;
using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.application.mapper;
using AdhdTimeOrganizer.domain.model.entity.activity.lookup;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activity.lookup.experienceType.command;

public class CreateActivityExperienceTypeEndpoint(AppDbContext dbContext, LookupMapper<ActivityExperienceType> mapper)
    : BaseCreateEndpoint<ActivityExperienceType, SelectOptionRequest, LookupMapper<ActivityExperienceType>>(dbContext, mapper);
