using AdhdTimeOrganizer.application.dto.response.generic;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.domain.model.entity.activity.lookup;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activity.lookup.locationType.query;

public class GetAllActivityLocationTypeEndpoint(AppDbContext dbContext)
    : BaseGetAllEndpoint<ActivityLocationType, LookupResponse<ActivityLocationType>>(dbContext);
