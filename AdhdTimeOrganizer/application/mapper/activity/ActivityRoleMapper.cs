using AdhdTimeOrganizer.application.dto.response;
using AdhdTimeOrganizer.application.dto.response.activity;
using AdhdTimeOrganizer.application.dto.response.generic;
using AdhdTimeOrganizer.application.mapper.@interface;
using AdhdTimeOrganizer.domain.model.entity.activity;
using Riok.Mapperly.Abstractions;

namespace AdhdTimeOrganizer.application.mapper.activity;

[Mapper]
public partial class ActivityRoleMapper : IBaseReadMapper<ActivityRole, RoleResponse>
{
    public partial RoleResponse ToResponse(ActivityRole entity);
    public partial SelectOptionResponse ToSelectOptionResponse(ActivityRole entity);
}
