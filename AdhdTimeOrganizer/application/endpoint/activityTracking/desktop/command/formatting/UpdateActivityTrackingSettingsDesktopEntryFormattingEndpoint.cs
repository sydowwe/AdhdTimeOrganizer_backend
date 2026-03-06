using AdhdTimeOrganizer.application.dto.request.activityTracking.desktop;
using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.application.endpointGroups;
using AdhdTimeOrganizer.application.mapper.activityTracking.desktop;
using AdhdTimeOrganizer.domain.model.entity.activityTracking.desktop;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activityTracking.desktop.command;

public class UpdateActivityTrackingSettingsDesktopEntryFormattingEndpoint(
    AppDbContext dbContext,
    ActivityTrackingSettingsDesktopEntryFormattingMapper mapper)
    : BaseUpdateEndpoint<ActivityTrackingSettingsDesktopEntryFormatting, ActivityTrackingDesktopSettingsEntryFormattingRequest, ActivityTrackingSettingsDesktopEntryFormattingMapper>(dbContext, mapper)
{
    public override string Route => "/entry-formatting";
    public override void Configure()
    {
        base.Configure();
        Group<ActivityTrackingDesktopSettingsGroup>();
    }
}