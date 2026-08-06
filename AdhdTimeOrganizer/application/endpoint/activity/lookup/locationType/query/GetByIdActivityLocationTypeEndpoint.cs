using AdhdTimeOrganizer.domain.model.entity.activity.lookup;
using AdhdTimeOrganizer.infrastructure.persistence;
using Sydowwe.Framework.application.dto.response.generic;
using Sydowwe.Framework.application.endpoint.@base.read;

namespace AdhdTimeOrganizer.application.endpoint.activity.lookup.locationType.query;

public class GetByIdActivityLocationTypeEndpoint(AppDbContext dbContext)
    : BaseGetByIdEndpoint<ActivityLocationType, LookupResponse<ActivityLocationType>>(dbContext)
{
    // Scoped by AppDbContext's global IEntityWithUser query filter, so a foreign row never projects.
    protected override Task<bool> AuthorizeAsync(LookupResponse<ActivityLocationType> entity, CancellationToken ct) => Task.FromResult(true);
}