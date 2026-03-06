using AdhdTimeOrganizer.application.dto.filter;
using AdhdTimeOrganizer.application.dto.response.activityTracking.desktop;
using AdhdTimeOrganizer.application.endpoint.@base.read.withoutUser;
using AdhdTimeOrganizer.application.endpointGroups;
using AdhdTimeOrganizer.application.mapper.activityTracking.desktop;
using AdhdTimeOrganizer.domain.model.entity.activityTracking.desktop;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activityTracking.desktop.query;

public class FetchTableActivityTrackingDesktopSettingsEntryFormattingEndpoint(
    AppDbContext dbContext,
    ActivityTrackingSettingsDesktopEntryFormattingMapper mapper)
    : BaseWithoutUserFilteredPaginatedEndpoint<
        ActivityTrackingSettingsDesktopEntryFormatting,
        ActivityTrackingDesktopSettingsEntryFormattingResponse,
        ActivityTrackingDesktopSettingsEntryFormattingFilterRequest,
        ActivityTrackingSettingsDesktopEntryFormattingMapper>(dbContext, mapper)
{
    protected override IQueryable<ActivityTrackingSettingsDesktopEntryFormatting> ApplyCustomFiltering(
        IQueryable<ActivityTrackingSettingsDesktopEntryFormatting> query,
        ActivityTrackingDesktopSettingsEntryFormattingFilterRequest filter)
    {
        if (!string.IsNullOrWhiteSpace(filter.ProcessKey))
            query = query.Where(x => x.ProcessKey.Contains(filter.ProcessKey));

        return query;
    }
    public override void Configure()
    {
        base.Configure();
        Group<ActivityTrackingDesktopSettingsGroup>();
    }
}
