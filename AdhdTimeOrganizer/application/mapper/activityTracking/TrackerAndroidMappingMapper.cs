using AdhdTimeOrganizer.application.dto.request.activityTracking.android;
using AdhdTimeOrganizer.application.dto.response.activity;
using AdhdTimeOrganizer.application.dto.response.activityTracking.android;
using AdhdTimeOrganizer.application.dto.response.generic;
using AdhdTimeOrganizer.application.mapper.@interface;
using AdhdTimeOrganizer.domain.model.entity.activity;
using AdhdTimeOrganizer.domain.model.entity.activityTracking.android;
using Riok.Mapperly.Abstractions;

namespace AdhdTimeOrganizer.application.mapper.activityTracking;

[Mapper]
public partial class TrackerAndroidMappingMapper :
    IBaseCreateMapper<TrackerAndroidMappingByPattern, CreateTrackerAndroidMappingRequest>,
    IBaseUpdateMapper<TrackerAndroidMappingByPattern, UpdateTrackerAndroidMappingRequest>,
    IBaseReadMapper<TrackerAndroidMappingByPattern, TrackerAndroidMappingResponse>
{
    public partial TrackerAndroidMappingResponse ToResponse(TrackerAndroidMappingByPattern entity);

    public partial TrackerAndroidMappingByPattern ToEntity(CreateTrackerAndroidMappingRequest request, long userId);

    public partial void UpdateEntity(UpdateTrackerAndroidMappingRequest request, TrackerAndroidMappingByPattern entity);

    public partial IQueryable<TrackerAndroidMappingResponse> ProjectToResponse(IQueryable<TrackerAndroidMappingByPattern> query);

    public partial SelectOptionResponse ToSelectOptionResponse(TrackerAndroidMappingByPattern entity);

    private static ActivityFilterFormResponse MapActivity(Activity activity) => new()
    {
        Id = activity.Id,
        Text = activity.Name,
        RoleId = activity.RoleId,
        CategoryId = activity.CategoryId,
    };
}
