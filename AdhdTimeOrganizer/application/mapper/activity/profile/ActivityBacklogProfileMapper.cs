using AdhdTimeOrganizer.application.dto.request.activity.profile;
using AdhdTimeOrganizer.application.dto.response.activity.profile;
using AdhdTimeOrganizer.application.mapper.@interface;
using AdhdTimeOrganizer.domain.model.entity.activity.profile;
using Riok.Mapperly.Abstractions;

namespace AdhdTimeOrganizer.application.mapper.activity.profile;

[Mapper]
public partial class ActivityBacklogProfileMapper : IBaseResponseMapper<ActivityBacklogProfile, ActivityBacklogProfileResponse>
{
    public partial ActivityBacklogProfileResponse ToResponse(ActivityBacklogProfile entity);
    public partial IQueryable<ActivityBacklogProfileResponse> ProjectToResponse(IQueryable<ActivityBacklogProfile> source);

    [MapperIgnoreTarget(nameof(ActivityBacklogProfile.Activity))]
    [MapperIgnoreTarget(nameof(ActivityBacklogProfile.LocationType))]
    [MapperIgnoreTarget(nameof(ActivityBacklogProfile.WeatherDependency))]
    [MapperIgnoreTarget(nameof(ActivityBacklogProfile.ExpectedCostTier))]
    public partial ActivityBacklogProfile ToEntity(ActivityBacklogProfileRequest request);

    [MapperIgnoreTarget(nameof(ActivityBacklogProfile.ActivityId))]
    [MapperIgnoreTarget(nameof(ActivityBacklogProfile.Activity))]
    [MapperIgnoreTarget(nameof(ActivityBacklogProfile.LocationType))]
    [MapperIgnoreTarget(nameof(ActivityBacklogProfile.WeatherDependency))]
    [MapperIgnoreTarget(nameof(ActivityBacklogProfile.ExpectedCostTier))]
    public partial void UpdateEntity(ActivityBacklogProfileRequest request, ActivityBacklogProfile entity);
}
