using AdhdTimeOrganizer.ActivityProfiles.application.dto.request;
using AdhdTimeOrganizer.ActivityProfiles.application.validator;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using AdhdTimeOrganizer.Core.domain.serviceContract;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.endpoint.@base.command;
using Sydowwe.Framework.application.extensions;

namespace AdhdTimeOrganizer.ActivityProfiles.application.endpoint.project.command;

public class PatchActivityProjectProfileStatusEndpoint(DbContext dbContext, IReferencedIdOwnershipService ownership)
    : BasePatchEndpoint<ActivityProjectProfile, PatchActivityProjectProfileStatusRequest>(dbContext)
{
    private readonly DbContext _dbContext = dbContext;

    /// <summary>
    /// Re-routes off the base's <c>/{id}</c> onto <c>/{id}/status</c>: the readiness board flips one enum
    /// per click, so this sits beside the full update rather than replacing it.
    /// </summary>
    public override void Configure()
    {
        base.Configure();
        Patch("/activity-project-profile/{id:long}/status");
        Validator<PatchActivityProjectProfileStatusValidator>();
        Summary(s =>
        {
            s.Summary = "Update ActivityProjectProfile readiness status";
            s.Description = "Status-only patch: sets ReadinessStatus and nothing else. No side effects.";
            s.Response(204, "Success");
            s.Response(404, "Not found");
            s.Response(400, "Bad request");
        });
    }

    protected override void Mapping(ActivityProjectProfile entity, PatchActivityProjectProfileStatusRequest req) =>
        entity.ReadinessStatus = req.ReadinessStatus;

    /// <summary>Ownership through the Activity — see <c>UpdateActivityBacklogProfileEndpoint</c>.</summary>
    protected override Task<bool> AuthorizeAsync(ActivityProjectProfile entity, CancellationToken ct = default) =>
        ownership.OwnsActivityAsync(_dbContext, User.GetId(), entity.ActivityId, ct);
}
