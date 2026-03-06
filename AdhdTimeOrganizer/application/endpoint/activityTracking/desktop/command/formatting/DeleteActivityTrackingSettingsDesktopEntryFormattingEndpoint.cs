using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.application.endpointGroups;
using AdhdTimeOrganizer.domain.model.entity.activityTracking.desktop;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activityTracking.desktop.command.formatting;

public class DeleteActivityTrackingSettingsDesktopEntryFormattingEndpoint(AppDbContext dbContext)
    : BaseDeleteEndpoint<ActivityTrackingSettingsDesktopEntryFormatting>(dbContext)
{
    public override string Route => "/entry-formatting";
    public override void Configure()
    {
        base.Configure();
        Group<ActivityTrackingDesktopSettingsGroup>();
    }
}
