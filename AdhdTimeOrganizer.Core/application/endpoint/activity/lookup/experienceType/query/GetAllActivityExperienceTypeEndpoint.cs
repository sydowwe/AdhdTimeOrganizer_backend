using AdhdTimeOrganizer.Core.domain.model.entity.activity.lookup;
using Sydowwe.Framework.application.dto.response.generic;
using Sydowwe.Framework.application.endpoint.@base.read;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.Core.application.endpoint.activity.lookup.experienceType.query;

public class GetAllActivityExperienceTypeEndpoint(DbContext dbContext)
    : BaseGetAllLookupEndpoint<ActivityExperienceType>(dbContext);