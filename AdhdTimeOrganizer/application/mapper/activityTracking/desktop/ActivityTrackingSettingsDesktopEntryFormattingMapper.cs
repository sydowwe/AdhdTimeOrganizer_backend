using AdhdTimeOrganizer.application.dto.request.activityTracking.desktop;
using AdhdTimeOrganizer.application.dto.response.activityTracking.desktop;
using AdhdTimeOrganizer.application.dto.response.generic;
using AdhdTimeOrganizer.application.mapper.@interface;
using AdhdTimeOrganizer.domain.model.entity.activityTracking.desktop;
using Riok.Mapperly.Abstractions;

namespace AdhdTimeOrganizer.application.mapper.activityTracking.desktop;

[Mapper]
public partial class ActivityTrackingSettingsDesktopEntryFormattingMapper :
    IBaseReadMapper<ActivityTrackingSettingsDesktopEntryFormatting, ActivityTrackingDesktopSettingsEntryFormattingResponse>,
    IBaseCreateMapper<ActivityTrackingSettingsDesktopEntryFormatting, ActivityTrackingDesktopSettingsEntryFormattingRequest>,
    IBaseUpdateMapper<ActivityTrackingSettingsDesktopEntryFormatting, ActivityTrackingDesktopSettingsEntryFormattingRequest>
{
    public partial ActivityTrackingDesktopSettingsEntryFormattingResponse ToResponse(ActivityTrackingSettingsDesktopEntryFormatting entity);

    public SelectOptionResponse ToSelectOptionResponse(ActivityTrackingSettingsDesktopEntryFormatting entity)
        => new(entity.Id, entity.ProcessKey);

    public partial IQueryable<ActivityTrackingDesktopSettingsEntryFormattingResponse> ProjectToResponse(IQueryable<ActivityTrackingSettingsDesktopEntryFormatting> query);

    public partial ActivityTrackingSettingsDesktopEntryFormatting ToEntity(ActivityTrackingDesktopSettingsEntryFormattingRequest request, long userId);

    public partial void UpdateEntity(ActivityTrackingDesktopSettingsEntryFormattingRequest request, ActivityTrackingSettingsDesktopEntryFormatting entity);
}
