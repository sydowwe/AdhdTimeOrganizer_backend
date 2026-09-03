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
    /// Two checks the validator cannot make, both needing the request scope's <c>DbContext</c> and the
    /// principal: whether the target ids are the caller's own (SEC-2), and whether the caller already has
    /// a rule for this pattern (CQ-11). Ownership first — a foreign id is a 404 that must not be
    /// distinguishable from a missing one, and reporting a duplicate before it would leak that the ids
    /// exist.
    /// </summary>
    protected override async Task<bool> BeforeMapping(CreateTrackerDesktopMappingRequest req, CancellationToken ct = default)
    {
        if (!await this.EnsureOwnsMappingTargetsAsync(ownership, DbContext, req, ct))
            return false;

        return await this.EnsureMappingPatternIsUniqueAsync<TrackerDesktopMappingByPattern>(
            DbContext,
            e => e.ProcessName == req.ProcessName && e.ProductName == req.ProductName && e.WindowTitle == req.WindowTitle,
            excludedId: null,
            MappingPatternUniquenessExtensions.DescribePattern(
                ("ProcessName", req.ProcessName), ("ProductName", req.ProductName), ("WindowTitle", req.WindowTitle)),
            ct);
    }
}
