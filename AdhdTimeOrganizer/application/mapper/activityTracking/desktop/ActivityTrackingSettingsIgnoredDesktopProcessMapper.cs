using AdhdTimeOrganizer.application.dto.request.activityTracking.desktop;
using AdhdTimeOrganizer.application.dto.response.activityTracking.desktop;
using AdhdTimeOrganizer.application.dto.response.generic;
using AdhdTimeOrganizer.application.mapper.@interface;
using AdhdTimeOrganizer.domain.model.entity.activityTracking.desktop;
using Riok.Mapperly.Abstractions;

namespace AdhdTimeOrganizer.application.mapper.activityTracking.desktop;

[Mapper]
public partial class ActivityTrackingSettingsIgnoredDesktopProcessMapper :
    IBaseReadMapper<ActivityTrackingSettingsDesktopIgnoredProcess, ActivityTrackingDesktopSettingsIgnoredProcessResponse>,
    IBaseCreateMapper<ActivityTrackingSettingsDesktopIgnoredProcess, ActivityTrackingSettingsDesktopIgnoredProcessRequest>,
    IBaseUpdateMapper<ActivityTrackingSettingsDesktopIgnoredProcess, ActivityTrackingSettingsDesktopIgnoredProcessRequest>
{
    public partial ActivityTrackingDesktopSettingsIgnoredProcessResponse ToResponse(ActivityTrackingSettingsDesktopIgnoredProcess entity);

    public SelectOptionResponse ToSelectOptionResponse(ActivityTrackingSettingsDesktopIgnoredProcess entity)
        => new(entity.Id, entity.ProcessKey);

    public partial IQueryable<ActivityTrackingDesktopSettingsIgnoredProcessResponse> ProjectToResponse(IQueryable<ActivityTrackingSettingsDesktopIgnoredProcess> query);

    public partial ActivityTrackingSettingsDesktopIgnoredProcess ToEntity(ActivityTrackingSettingsDesktopIgnoredProcessRequest request, long userId);

    public partial void UpdateEntity(ActivityTrackingSettingsDesktopIgnoredProcessRequest request, ActivityTrackingSettingsDesktopIgnoredProcess entity);
}
