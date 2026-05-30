using AdhdTimeOrganizer.application.dto.request.activityTracking.desktop;
using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.application.endpointGroups;
using AdhdTimeOrganizer.application.validator;
using AdhdTimeOrganizer.domain.model.entity.activityTracking.desktop;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activityTracking.desktop.command;

public class UpdateTrackerDesktopMappingEndpoint(AppDbContext dbContext)
    : BaseUpdateEndpoint<TrackerDesktopMappingByPattern, UpdateTrackerDesktopMappingRequest>(dbContext)
{
    public override void Configure()
    {
        base.Configure();
        Validator<UpdateTrackerDesktopMappingValidator>();
        Group<ActivityTrackingDesktopSettingsGroup>();
    }
}
