using AdhdTimeOrganizer.application.dto.request.activity.profile;
using AdhdTimeOrganizer.application.dto.response.activity.profile;
using AdhdTimeOrganizer.application.mapper.@interface;
using AdhdTimeOrganizer.domain.model.entity.activity.profile;
using Riok.Mapperly.Abstractions;

namespace AdhdTimeOrganizer.application.mapper.activity.profile;

[Mapper]
public partial class ActivityProjectProfileMapper : IBaseResponseMapper<ActivityProjectProfile, ActivityProjectProfileResponse>
{
    public partial ActivityProjectProfileResponse ToResponse(ActivityProjectProfile entity);
    public partial IQueryable<ActivityProjectProfileResponse> ProjectToResponse(IQueryable<ActivityProjectProfile> source);

    [MapperIgnoreTarget(nameof(ActivityProjectProfile.Activity))]
    public partial ActivityProjectProfile ToEntity(ActivityProjectProfileRequest request);

    [MapperIgnoreTarget(nameof(ActivityProjectProfile.ActivityId))]
    [MapperIgnoreTarget(nameof(ActivityProjectProfile.Activity))]
    public partial void UpdateEntity(ActivityProjectProfileRequest request, ActivityProjectProfile entity);
}
