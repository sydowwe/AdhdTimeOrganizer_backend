using AdhdTimeOrganizer.application.dto.request.activityTracking.desktop;
using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.application.endpointGroups;
using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.application.helper;
using AdhdTimeOrganizer.application.mapper.activityTracking.desktop;
using AdhdTimeOrganizer.domain.model.entity.activityTracking.desktop;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.activityTracking.desktop.command;

public class CreateActivityTrackingSettingsDesktopIgnoredProcessEndpoint(
    AppDbContext dbContext,
    ActivityTrackingSettingsIgnoredDesktopProcessMapper mapper)
    : BaseCreateEndpoint<ActivityTrackingSettingsDesktopIgnoredProcess, ActivityTrackingSettingsDesktopIgnoredProcessRequest, ActivityTrackingSettingsIgnoredDesktopProcessMapper>(dbContext, mapper)
{
    public override string Route => "/ignored-processes";
    public override void Configure()
    {
        base.Configure();
        Group<ActivityTrackingDesktopSettingsGroup>();
    }
}
