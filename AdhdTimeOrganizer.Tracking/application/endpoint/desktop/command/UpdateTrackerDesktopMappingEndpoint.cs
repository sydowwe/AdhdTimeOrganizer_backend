using AdhdTimeOrganizer.Core.domain.serviceContract;
using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking.desktop;
using AdhdTimeOrganizer.Tracking.application.endpoint.extensions;
using AdhdTimeOrganizer.Tracking.application.endpointGroups;
using AdhdTimeOrganizer.Tracking.application.validator;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking.desktop;
using FastEndpoints;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.Tracking.application.endpoint.activityTracking.desktop.command;

public class UpdateTrackerDesktopMappingEndpoint(DbContext dbContext, IReferencedIdOwnershipService ownership)
    : BaseUpdateEndpoint<TrackerDesktopMappingByPattern, UpdateTrackerDesktopMappingRequest>(dbContext)
{
    public override void Configure()
    {
        base.Configure();
        Validator<UpdateTrackerDesktopMappingValidator>();
        Group<ActivityTrackingDesktopSettingsGroup>();
    }

    /// <summary>
    /// The same two hooks the create endpoint runs, for the same reasons — and neither is optional here:
    /// the update DTO gained the three target ids in CQ-10, which is the surface SEC-2 would reopen on,
    /// and re-pointing a rule onto a pattern the user already has is the same duplicate as creating one.
    /// </summary>
    /// <remarks>
    /// The row's own id is excluded from the uniqueness check, or an update that leaves the pattern alone
    /// would collide with itself. The base runs this before it loads the row, so the id comes off the
    /// route rather than the entity — a mismatch is impossible, since the base loads by that same value.
    /// </remarks>
    protected override async Task<bool> BeforeMapping(UpdateTrackerDesktopMappingRequest req, CancellationToken ct = default)
    {
        if (!await this.EnsureOwnsMappingTargetsAsync(ownership, DbContext, req, ct))
            return false;

        return await this.EnsureMappingPatternIsUniqueAsync<TrackerDesktopMappingByPattern>(
            DbContext,
            e => e.ProcessName == req.ProcessName && e.ProductName == req.ProductName && e.WindowTitle == req.WindowTitle,
            Route<long>("id"),
            MappingPatternUniquenessExtensions.DescribePattern(
                ("ProcessName", req.ProcessName), ("ProductName", req.ProductName), ("WindowTitle", req.WindowTitle)),
            ct);
    }
}
