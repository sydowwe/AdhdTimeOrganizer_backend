using AdhdTimeOrganizer.Core.domain.serviceContract;
using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking.android;
using AdhdTimeOrganizer.Tracking.application.endpoint.extensions;
using AdhdTimeOrganizer.Tracking.application.endpointGroups;
using AdhdTimeOrganizer.Tracking.application.validator;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking.android;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.Tracking.application.endpoint.activityTracking.android.command;

public class CreateTrackerAndroidMappingEndpoint(DbContext dbContext, IReferencedIdOwnershipService ownership)
    : BaseCreateEndpoint<TrackerAndroidMappingByPattern, CreateTrackerAndroidMappingRequest>(dbContext)
{
    public override void Configure()
    {
        base.Configure();
        Validator<CreateTrackerAndroidMappingValidator>();
        Group<ActivityTrackingAndroidSettingsGroup>();
    }

    /// <summary>See the desktop twin: target ownership (SEC-2) then pattern uniqueness (CQ-11).</summary>
    protected override async Task<bool> BeforeMapping(CreateTrackerAndroidMappingRequest req, CancellationToken ct = default)
    {
        if (!await this.EnsureOwnsMappingTargetsAsync(ownership, DbContext, req, ct))
            return false;

        return await this.EnsureMappingPatternIsUniqueAsync<TrackerAndroidMappingByPattern>(
            DbContext,
            e => e.PackageName == req.PackageName && e.AppLabel == req.AppLabel,
            excludedId: null,
            MappingPatternUniquenessExtensions.DescribePattern(
                ("PackageName", req.PackageName), ("AppLabel", req.AppLabel)),
            ct);
    }
}
