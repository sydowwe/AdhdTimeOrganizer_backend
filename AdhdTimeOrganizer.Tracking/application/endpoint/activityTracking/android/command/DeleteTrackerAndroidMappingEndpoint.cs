using AdhdTimeOrganizer.Tracking.application.endpointGroups;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking.android;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.Tracking.application.endpoint.activityTracking.android.command;

public class DeleteTrackerAndroidMappingEndpoint(DbContext dbContext)
    : BaseDeleteEndpoint<TrackerAndroidMappingByPattern>(dbContext)
{
    public override void Configure()
    {
        base.Configure();
        Group<ActivityTrackingAndroidSettingsGroup>();
    }
}