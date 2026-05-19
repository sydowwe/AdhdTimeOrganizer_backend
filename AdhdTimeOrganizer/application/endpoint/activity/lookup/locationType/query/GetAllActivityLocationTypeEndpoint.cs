using AdhdTimeOrganizer.application.dto.response.generic;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.application.mapper;
using AdhdTimeOrganizer.domain.model.entity.activity.lookup;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activity.lookup.locationType.query;

public class GetAllActivityLocationTypeEndpoint(AppDbContext dbContext, LookupMapper<ActivityLocationType> mapper)
    : BaseGetAllEndpoint<ActivityLocationType, LookupResponse, LookupMapper<ActivityLocationType>>(dbContext, mapper);
