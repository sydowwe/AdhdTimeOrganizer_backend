using AdhdTimeOrganizer.application.dto.request.activityTracking.desktop;
using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.application.endpointGroups;
using AdhdTimeOrganizer.application.mapper.activityTracking;
using AdhdTimeOrganizer.application.validator;
using AdhdTimeOrganizer.domain.model.entity.activityTracking.desktop;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activityTracking.desktop.command;

public class UpdateTrackerDesktopMappingEndpoint(AppDbContext dbContext, TrackerDesktopMappingMapper mapper)
    : BaseUpdateEndpoint<TrackerDesktopMappingByPattern, UpdateTrackerDesktopMappingRequest, TrackerDesktopMappingMapper>(dbContext, mapper)
{
    public override void Configure()
    {
        base.Configure();
        Validator<UpdateTrackerDesktopMappingValidator>();
        Group<ActivityTrackingDesktopSettingsGroup>();
    }
}
