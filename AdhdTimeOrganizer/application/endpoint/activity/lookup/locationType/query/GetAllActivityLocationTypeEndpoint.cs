using AdhdTimeOrganizer.domain.model.entity.activity.lookup;
using AdhdTimeOrganizer.infrastructure.persistence;
using Sydowwe.Framework.application.dto.response.generic;
using Sydowwe.Framework.application.endpoint.@base.read;

namespace AdhdTimeOrganizer.application.endpoint.activity.lookup.locationType.query;

public class GetAllActivityLocationTypeEndpoint(AppDbContext dbContext)
    : BaseGetAllLookupEndpoint<ActivityLocationType>(dbContext);