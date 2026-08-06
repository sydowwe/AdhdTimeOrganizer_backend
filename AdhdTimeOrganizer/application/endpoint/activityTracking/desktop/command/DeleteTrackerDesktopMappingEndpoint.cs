using AdhdTimeOrganizer.application.endpointGroups;
using AdhdTimeOrganizer.domain.model.entity.activityTracking.desktop;
using AdhdTimeOrganizer.infrastructure.persistence;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.application.endpoint.activityTracking.desktop.command;

public class DeleteTrackerDesktopMappingEndpoint(AppDbContext dbContext)
    : BaseDeleteEndpoint<TrackerDesktopMappingByPattern>(dbContext)
{
    public override void Configure()
    {
        base.Configure();
        Group<ActivityTrackingDesktopSettingsGroup>();
    }
}