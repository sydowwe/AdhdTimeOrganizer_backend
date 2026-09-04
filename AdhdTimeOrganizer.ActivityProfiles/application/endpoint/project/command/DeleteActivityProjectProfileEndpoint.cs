using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using AdhdTimeOrganizer.Core.domain.serviceContract;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.endpoint.@base.command;
using Sydowwe.Framework.application.extensions;

namespace AdhdTimeOrganizer.ActivityProfiles.application.endpoint.project.command;

public class DeleteActivityProjectProfileEndpoint(DbContext dbContext, IReferencedIdOwnershipService ownership)
    : BaseDeleteEndpoint<ActivityProjectProfile>(dbContext)
{
    private readonly DbContext _dbContext = dbContext;

    /// <summary>Ownership through the Activity — see <c>UpdateActivityBacklogProfileEndpoint</c>.</summary>
    protected override Task<bool> AuthorizeAsync(ActivityProjectProfile entity, CancellationToken ct = default) =>
        ownership.OwnsActivityAsync(_dbContext, User.GetId(), entity.ActivityId, ct);
}
