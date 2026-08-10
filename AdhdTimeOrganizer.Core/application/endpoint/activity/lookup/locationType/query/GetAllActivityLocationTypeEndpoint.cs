using AdhdTimeOrganizer.Core.domain.model.entity.activity.lookup;
using Sydowwe.Framework.application.dto.response.generic;
using Sydowwe.Framework.application.endpoint.@base.read;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.Core.application.endpoint.activity.lookup.locationType.query;

public class GetAllActivityLocationTypeEndpoint(DbContext dbContext)
    : BaseGetAllLookupEndpoint<ActivityLocationType>(dbContext);