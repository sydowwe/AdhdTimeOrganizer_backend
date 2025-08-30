
using AdhdTimeOrganizer.application.dto.response.activity;
using AdhdTimeOrganizer.application.dto.response.generic;
using AdhdTimeOrganizer.application.mapper.@interface;
using AdhdTimeOrganizer.domain.model.entity.activity;
using Riok.Mapperly.Abstractions;

namespace AdhdTimeOrganizer.application.mapper.activity;

[Mapper]
public partial class ActivityMapper : IBaseReadMapper<Activity, ActivityResponse>
{
    public partial ActivityResponse ToResponse(Activity entity);
    public partial SelectOptionResponse ToSelectOptionResponse(Activity entity);
}
