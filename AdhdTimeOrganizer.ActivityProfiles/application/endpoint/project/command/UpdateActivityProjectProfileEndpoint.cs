using AdhdTimeOrganizer.ActivityProfiles.application.dto.request;
using AdhdTimeOrganizer.ActivityProfiles.application.validator;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using AdhdTimeOrganizer.Core.domain.serviceContract;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.endpoint.@base.command;
using Sydowwe.Framework.application.extensions;

namespace AdhdTimeOrganizer.ActivityProfiles.application.endpoint.project.command;

public class UpdateActivityProjectProfileEndpoint(DbContext dbContext, IReferencedIdOwnershipService ownership)
    : BaseUpdateEndpoint<ActivityProjectProfile, ActivityProjectProfileRequest>(dbContext)
{
    public override void Configure()
    {
        base.Configure();
        Validator<UpdateActivityProjectProfileValidator>();
    }

    /// <summary>Ownership through the Activity — see <c>UpdateActivityBacklogProfileEndpoint</c>.</summary>
    protected override Task<bool> AuthorizeAsync(ActivityProjectProfile entity, CancellationToken ct = default) =>
        ownership.OwnsActivityAsync(DbContext, User.GetId(), entity.ActivityId, ct);
}
