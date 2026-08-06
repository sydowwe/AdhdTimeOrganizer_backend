using AdhdTimeOrganizer.application.endpointGroups;
using AdhdTimeOrganizer.domain.model.entity.activityTracking.android;
using AdhdTimeOrganizer.infrastructure.persistence;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.application.endpoint.activityTracking.android.command;

public class DeleteTrackerAndroidMappingEndpoint(AppDbContext dbContext)
    : BaseDeleteEndpoint<TrackerAndroidMappingByPattern>(dbContext)
{
    public override void Configure()
    {
        base.Configure();
        Group<ActivityTrackingAndroidSettingsGroup>();
    }
}