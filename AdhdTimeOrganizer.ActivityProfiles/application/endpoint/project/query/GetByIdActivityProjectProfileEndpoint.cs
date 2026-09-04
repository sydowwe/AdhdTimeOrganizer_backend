using AdhdTimeOrganizer.ActivityProfiles.application.dto.response;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using AdhdTimeOrganizer.Core.domain.serviceContract;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.endpoint.@base.read;
using Sydowwe.Framework.application.extensions;

namespace AdhdTimeOrganizer.ActivityProfiles.application.endpoint.project.query;

public class GetByIdActivityProjectProfileEndpoint(DbContext dbContext, IReferencedIdOwnershipService ownership)
    : BaseGetByIdEndpoint<ActivityProjectProfile, ActivityProjectProfileResponse>(dbContext)
{
    private readonly DbContext _dbContext = dbContext;

    /// <summary>404 rather than 403 for a foreign row — see <c>GetByIdActivityBacklogProfileEndpoint</c>.</summary>
    protected override bool NotFoundWhenUnauthorized => true;

    /// <summary>Ownership through the Activity — see <c>GetByIdActivityBacklogProfileEndpoint</c>.</summary>
    protected override Task<bool> AuthorizeAsync(ActivityProjectProfileResponse entity, CancellationToken ct) =>
        ownership.OwnsActivityAsync(_dbContext, User.GetId(), entity.ActivityId, ct);
}
