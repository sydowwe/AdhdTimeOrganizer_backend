
using AdhdTimeOrganizer.application.dto.request.activity;
using AdhdTimeOrganizer.application.dto.response.activity;
using AdhdTimeOrganizer.application.dto.response.generic;
using AdhdTimeOrganizer.application.mapper.@interface;
using AdhdTimeOrganizer.domain.model.entity.activity;
using Riok.Mapperly.Abstractions;

namespace AdhdTimeOrganizer.application.mapper.activity;

[Mapper]
public partial class ActivityMapper : IBaseSimpleCrudMapper<Activity, ActivityRequest, ActivityResponse>
{
    public partial ActivityResponse ToResponse(Activity entity);
    public partial SelectOptionResponse ToSelectOptionResponse(Activity entity);
    public partial Activity ToEntity(ActivityRequest request, long userId);

    public partial void UpdateEntity(ActivityRequest request, Activity entity);

    public partial IQueryable<ActivityResponse> ProjectToResponse(IQueryable<Activity> query);

}
