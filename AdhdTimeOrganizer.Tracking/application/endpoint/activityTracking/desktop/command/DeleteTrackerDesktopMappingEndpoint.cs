using AdhdTimeOrganizer.Tracking.application.endpointGroups;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking.desktop;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.Tracking.application.endpoint.activityTracking.desktop.command;

public class DeleteTrackerDesktopMappingEndpoint(DbContext dbContext)
    : BaseDeleteEndpoint<TrackerDesktopMappingByPattern>(dbContext)
{
    public override void Configure()
    {
        base.Configure();
        Group<ActivityTrackingDesktopSettingsGroup>();
    }
}