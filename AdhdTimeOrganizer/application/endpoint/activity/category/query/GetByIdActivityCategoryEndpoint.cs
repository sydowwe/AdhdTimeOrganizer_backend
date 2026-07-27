using AdhdTimeOrganizer.application.dto.response.activity;
using AdhdTimeOrganizer.domain.model.entity.activity;
using AdhdTimeOrganizer.infrastructure.persistence;
using Sydowwe.Framework.application.endpoint.@base.read;

namespace AdhdTimeOrganizer.application.endpoint.activity.category.query;

public class GetByIdActivityCategoryEndpoint(
    AppDbContext dbContext)
    : BaseGetByIdEndpoint<ActivityCategory, ActivityCategoryResponse>(dbContext)
{
    // Scoped by AppDbContext's global IEntityWithUser query filter, so a foreign row never projects.
    protected override Task<bool> AuthorizeAsync(ActivityCategoryResponse entity, CancellationToken ct) => Task.FromResult(true);
}