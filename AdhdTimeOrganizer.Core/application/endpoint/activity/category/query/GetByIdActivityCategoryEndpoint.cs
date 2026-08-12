using AdhdTimeOrganizer.Core.application.dto.response.activity;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.endpoint.@base.read;

namespace AdhdTimeOrganizer.Core.application.endpoint.activity.category.query;

public class GetByIdActivityCategoryEndpoint(
    DbContext dbContext)
    : BaseGetByIdEndpoint<ActivityCategory, ActivityCategoryResponse>(dbContext)
{
    // Scoped by AppDbContext's global IEntityWithUser query filter, so a foreign row never projects.
    protected override Task<bool> AuthorizeAsync(ActivityCategoryResponse entity, CancellationToken ct) => Task.FromResult(true);
}