using AdhdTimeOrganizer.application.dto.request.activityTracking.desktop;
using AdhdTimeOrganizer.application.dto.response.activity;
using AdhdTimeOrganizer.application.dto.response.activityTracking.desktop;
using AdhdTimeOrganizer.application.dto.response.generic;
using AdhdTimeOrganizer.application.mapper.@interface;
using AdhdTimeOrganizer.domain.model.entity.activity;
using AdhdTimeOrganizer.domain.model.entity.activityTracking.desktop;
using Riok.Mapperly.Abstractions;

namespace AdhdTimeOrganizer.application.mapper.activityTracking;

[Mapper]
public partial class TrackerDesktopMappingMapper :
    IBaseCreateMapper<TrackerDesktopMappingByPattern, CreateTrackerDesktopMappingRequest>,
    IBaseUpdateMapper<TrackerDesktopMappingByPattern, UpdateTrackerDesktopMappingRequest>,
    IBaseReadMapper<TrackerDesktopMappingByPattern, TrackerDesktopMappingResponse>
{
    public partial TrackerDesktopMappingResponse ToResponse(TrackerDesktopMappingByPattern entity);

    public partial TrackerDesktopMappingByPattern ToEntity(CreateTrackerDesktopMappingRequest request, long userId);

    // Only maps pattern fields — IsIgnored/ActivityId/RoleId/CategoryId not in source so they're left unchanged
    public partial void UpdateEntity(UpdateTrackerDesktopMappingRequest request, TrackerDesktopMappingByPattern entity);

    public partial IQueryable<TrackerDesktopMappingResponse> ProjectToResponse(IQueryable<TrackerDesktopMappingByPattern> query);

    public partial SelectOptionResponse ToSelectOptionResponse(TrackerDesktopMappingByPattern entity);

    private static ActivityFilterFormResponse MapActivity(Activity activity) => new()
    {
        Id = activity.Id,
        Text = activity.Name,
        RoleId = activity.RoleId,
        CategoryId = activity.CategoryId,
    };
}
