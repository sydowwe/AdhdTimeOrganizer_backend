using AdhdTimeOrganizer.domain.model.entity.activity.lookup;
using AdhdTimeOrganizer.infrastructure.persistence;
using Sydowwe.Framework.application.dto.response.generic;
using Sydowwe.Framework.application.endpoint.@base.read;

namespace AdhdTimeOrganizer.application.endpoint.activity.lookup.experienceType.query;

public class GetByIdActivityExperienceTypeEndpoint(AppDbContext dbContext)
    : BaseGetByIdEndpoint<ActivityExperienceType, LookupResponse<ActivityExperienceType>>(dbContext)
{
    // Scoped by AppDbContext's global IEntityWithUser query filter, so a foreign row never projects.
    protected override Task<bool> AuthorizeAsync(LookupResponse<ActivityExperienceType> entity, CancellationToken ct) => Task.FromResult(true);
}