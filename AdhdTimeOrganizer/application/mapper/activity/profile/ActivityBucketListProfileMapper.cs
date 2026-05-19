using AdhdTimeOrganizer.application.dto.request.activity.profile;
using AdhdTimeOrganizer.application.dto.response.activity.profile;
using AdhdTimeOrganizer.application.mapper.@interface;
using AdhdTimeOrganizer.domain.model.entity.activity.profile;
using Riok.Mapperly.Abstractions;

namespace AdhdTimeOrganizer.application.mapper.activity.profile;

[Mapper]
public partial class ActivityBucketListProfileMapper : IBaseResponseMapper<ActivityBucketListProfile, ActivityBucketListProfileResponse>
{
    public partial ActivityBucketListProfileResponse ToResponse(ActivityBucketListProfile entity);
    public partial IQueryable<ActivityBucketListProfileResponse> ProjectToResponse(IQueryable<ActivityBucketListProfile> source);

    [MapperIgnoreTarget(nameof(ActivityBucketListProfile.Activity))]
    [MapperIgnoreTarget(nameof(ActivityBucketListProfile.ExperienceType))]
    public partial ActivityBucketListProfile ToEntity(ActivityBucketListProfileRequest request);

    [MapperIgnoreTarget(nameof(ActivityBucketListProfile.ActivityId))]
    [MapperIgnoreTarget(nameof(ActivityBucketListProfile.Activity))]
    [MapperIgnoreTarget(nameof(ActivityBucketListProfile.ExperienceType))]
    public partial void UpdateEntity(ActivityBucketListProfileRequest request, ActivityBucketListProfile entity);
}
