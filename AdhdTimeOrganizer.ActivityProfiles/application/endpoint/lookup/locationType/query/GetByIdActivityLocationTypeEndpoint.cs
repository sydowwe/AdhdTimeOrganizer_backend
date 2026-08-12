using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.dto.response.generic;
using Sydowwe.Framework.application.endpoint.@base.read;

namespace AdhdTimeOrganizer.ActivityProfiles.application.endpoint.lookup.locationType.query;

public class GetByIdActivityLocationTypeEndpoint(DbContext dbContext)
    : BaseGetByIdEndpoint<ActivityLocationType, LookupResponse<ActivityLocationType>>(dbContext)
{
    // Scoped by AppDbContext's global IEntityWithUser query filter, so a foreign row never projects.
    protected override Task<bool> AuthorizeAsync(LookupResponse<ActivityLocationType> entity, CancellationToken ct) => Task.FromResult(true);
}