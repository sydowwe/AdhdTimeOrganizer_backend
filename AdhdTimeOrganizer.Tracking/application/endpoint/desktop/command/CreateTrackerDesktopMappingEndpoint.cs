using AdhdTimeOrganizer.Core.domain.serviceContract;
using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking.desktop;
using AdhdTimeOrganizer.Tracking.application.endpoint.extensions;
using AdhdTimeOrganizer.Tracking.application.endpointGroups;
using AdhdTimeOrganizer.Tracking.application.validator;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking.desktop;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.Tracking.application.endpoint.activityTracking.desktop.command;

public class CreateTrackerDesktopMappingEndpoint(DbContext dbContext, IReferencedIdOwnershipService ownership)
    : BaseCreateEndpoint<TrackerDesktopMappingByPattern, CreateTrackerDesktopMappingRequest>(dbContext)
{
    public override void Configure()
    {
        base.Configure();
        Validator<CreateTrackerDesktopMappingValidator>();
        Group<ActivityTrackingDesktopSettingsGroup>();
    }

    /// <summary>
    /// The validator can only check the shape of the target group; whether the ids in it are the
    /// caller's own needs the request scope's <c>DbContext</c> and the principal, so it happens here.
    /// </summary>
    protected override Task<bool> BeforeMapping(CreateTrackerDesktopMappingRequest req, CancellationToken ct = default) =>
        this.EnsureOwnsMappingTargetsAsync(ownership, DbContext, req.ActivityId, req.RoleId, req.CategoryId, ct);
}
