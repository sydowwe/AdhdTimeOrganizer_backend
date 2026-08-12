using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.dto.response.generic;
using Sydowwe.Framework.application.endpoint.@base.read;

namespace AdhdTimeOrganizer.ActivityProfiles.application.endpoint.lookup.experienceType.query;

public class GetByIdActivityExperienceTypeEndpoint(DbContext dbContext)
    : BaseGetByIdEndpoint<ActivityExperienceType, LookupResponse<ActivityExperienceType>>(dbContext)
{
    // Scoped by AppDbContext's global IEntityWithUser query filter, so a foreign row never projects.
    protected override Task<bool> AuthorizeAsync(LookupResponse<ActivityExperienceType> entity, CancellationToken ct) => Task.FromResult(true);
}