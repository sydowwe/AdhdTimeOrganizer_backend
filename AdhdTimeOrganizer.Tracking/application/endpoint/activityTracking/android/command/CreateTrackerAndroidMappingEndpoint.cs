using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking.android;
using AdhdTimeOrganizer.Tracking.application.endpointGroups;
using AdhdTimeOrganizer.Core.application.validator;
using AdhdTimeOrganizer.Tracking.application.validator;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking.android;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.Tracking.application.endpoint.activityTracking.android.command;

public class CreateTrackerAndroidMappingEndpoint(DbContext dbContext)
    : BaseCreateEndpoint<TrackerAndroidMappingByPattern, CreateTrackerAndroidMappingRequest>(dbContext)
{
    public override void Configure()
    {
        base.Configure();
        Validator<CreateTrackerAndroidMappingValidator>();
        Group<ActivityTrackingAndroidSettingsGroup>();
    }
}