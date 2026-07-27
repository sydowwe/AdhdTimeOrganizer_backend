using AdhdTimeOrganizer.application.dto.response.activity;
using AdhdTimeOrganizer.domain.model.entity.activity;
using AdhdTimeOrganizer.infrastructure.persistence;
using Sydowwe.Framework.application.endpoint.@base.read;

namespace AdhdTimeOrganizer.application.endpoint.activity.role.query;

public class GetByIdActivityRoleEndpoint(
    AppDbContext dbContext)
    : BaseGetByIdEndpoint<ActivityRole, ActivityRoleResponse>(dbContext)
{
    // Scoped by AppDbContext's global IEntityWithUser query filter, so a foreign row never projects.
    protected override Task<bool> AuthorizeAsync(ActivityRoleResponse entity, CancellationToken ct) => Task.FromResult(true);
}