using AdhdTimeOrganizer.application.dto.filter;
using AdhdTimeOrganizer.application.dto.response.activityTracking.desktop;
using AdhdTimeOrganizer.application.endpoint.@base.read.withoutUser;
using AdhdTimeOrganizer.application.endpointGroups;
using AdhdTimeOrganizer.application.mapper.activityTracking.desktop;
using AdhdTimeOrganizer.domain.model.entity.activityTracking.desktop;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activityTracking.desktop.query;

public class FetchTableActivityTrackingDesktopSettingsIgnoredProcessEndpoint(
    AppDbContext dbContext,
    ActivityTrackingSettingsIgnoredDesktopProcessMapper mapper)
    : BaseWithoutUserFilteredPaginatedEndpoint<
        ActivityTrackingSettingsDesktopIgnoredProcess,
        ActivityTrackingDesktopSettingsIgnoredProcessResponse,
        ActivityTrackingDesktopSettingsIgnoredProcessFilterRequest,
        ActivityTrackingSettingsIgnoredDesktopProcessMapper>(dbContext, mapper)
{
    protected override IQueryable<ActivityTrackingSettingsDesktopIgnoredProcess> ApplyCustomFiltering(
        IQueryable<ActivityTrackingSettingsDesktopIgnoredProcess> query,
        ActivityTrackingDesktopSettingsIgnoredProcessFilterRequest filter)
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
