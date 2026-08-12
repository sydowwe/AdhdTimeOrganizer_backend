using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking.desktop;
using AdhdTimeOrganizer.Tracking.application.endpointGroups;
using AdhdTimeOrganizer.Core.application.validator;
using AdhdTimeOrganizer.Tracking.application.validator;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking.desktop;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.Tracking.application.endpoint.activityTracking.desktop.command;

public class CreateTrackerDesktopMappingEndpoint(DbContext dbContext)
    : BaseCreateEndpoint<TrackerDesktopMappingByPattern, CreateTrackerDesktopMappingRequest>(dbContext)
{
    public override void Configure()
    {
        base.Configure();
        Validator<CreateTrackerDesktopMappingValidator>();
        Group<ActivityTrackingDesktopSettingsGroup>();
    }
}