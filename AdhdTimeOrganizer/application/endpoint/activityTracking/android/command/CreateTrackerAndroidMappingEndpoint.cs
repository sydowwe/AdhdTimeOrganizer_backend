using AdhdTimeOrganizer.application.dto.request.activityTracking.android;
using AdhdTimeOrganizer.application.endpointGroups;
using AdhdTimeOrganizer.application.validator;
using AdhdTimeOrganizer.domain.model.entity.activityTracking.android;
using AdhdTimeOrganizer.infrastructure.persistence;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.application.endpoint.activityTracking.android.command;

public class CreateTrackerAndroidMappingEndpoint(AppDbContext dbContext)
    : BaseCreateEndpoint<TrackerAndroidMappingByPattern, CreateTrackerAndroidMappingRequest>(dbContext)
{
    public override void Configure()
    {
        base.Configure();
        Validator<CreateTrackerAndroidMappingValidator>();
        Group<ActivityTrackingAndroidSettingsGroup>();
    }
}