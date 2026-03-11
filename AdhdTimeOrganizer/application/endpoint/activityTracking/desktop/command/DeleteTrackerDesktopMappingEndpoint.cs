using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.application.endpointGroups;
using AdhdTimeOrganizer.domain.model.entity.activityTracking.desktop;
using AdhdTimeOrganizer.infrastructure.persistence;

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
