using AdhdTimeOrganizer.ActivityProfiles.application.dto.request;
using AdhdTimeOrganizer.ActivityProfiles.application.validator;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using AdhdTimeOrganizer.Core.application.endpoint.extensions;
using AdhdTimeOrganizer.Core.domain.serviceContract;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.ActivityProfiles.application.endpoint.memoryAnchor.command;

public class UpdateMemoryAnchorEndpoint(DbContext dbContext, IReferencedIdOwnershipService ownership)
    : BaseUpdateEndpoint<MemoryAnchor, MemoryAnchorRequest>(dbContext)
{
    public override void Configure()
    {
        base.Configure();
        Validator<UpdateMemoryAnchorValidator>();
    }

    /// <summary>
    /// <c>UpdateEntity</c> writes <c>ActivityId</c>, so an edit can repoint an anchor the caller owns at
    /// another user's activity just as easily as a create can. Same guard, before the row is loaded.
    /// </summary>
    protected override Task<bool> BeforeMapping(MemoryAnchorRequest req, CancellationToken ct = default) =>
        this.EnsureOwnsActivityAsync(req, ownership, DbContext, ct);
}