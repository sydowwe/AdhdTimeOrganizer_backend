using AdhdTimeOrganizer.ActivityProfiles.application.dto.request;
using AdhdTimeOrganizer.ActivityProfiles.application.validator;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using AdhdTimeOrganizer.Core.domain.serviceContract;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.endpoint.@base.command;
using Sydowwe.Framework.application.extensions;

namespace AdhdTimeOrganizer.ActivityProfiles.application.endpoint.backlog.command;

public class UpdateActivityBacklogProfileEndpoint(DbContext dbContext, IReferencedIdOwnershipService ownership)
    : BaseUpdateEndpoint<ActivityBacklogProfile, ActivityBacklogProfileRequest>(dbContext)
{
    public override void Configure()
    {
        base.Configure();
        Validator<UpdateActivityBacklogProfileValidator>();
    }

    /// <summary>
    /// Ownership, reached through the Activity — the profile carries no <c>UserId</c> of its own. Belt and
    /// braces on purpose: the host's <c>AppQueryFilters.ActivityProfileUserScope</c> already keeps another
    /// account's row out of the base's load, but this slice cannot see that filter and does not depend on it.
    /// </summary>
    protected override Task<bool> AuthorizeAsync(ActivityBacklogProfile entity, CancellationToken ct = default) =>
        ownership.OwnsActivityAsync(DbContext, User.GetId(), entity.ActivityId, ct);
}
