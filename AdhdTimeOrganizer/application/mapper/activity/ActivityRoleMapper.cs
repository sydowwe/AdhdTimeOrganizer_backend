using AdhdTimeOrganizer.application.dto.request.@base;
using AdhdTimeOrganizer.application.dto.response;
using AdhdTimeOrganizer.application.dto.response.activity;
using AdhdTimeOrganizer.application.dto.response.generic;
using AdhdTimeOrganizer.application.mapper.@interface;
using AdhdTimeOrganizer.domain.model.entity.activity;
using Riok.Mapperly.Abstractions;

namespace AdhdTimeOrganizer.application.mapper.activity;

[Mapper]
public partial class ActivityRoleMapper : IBaseSimpleCrudMapper<ActivityRole, NameTextColorIconRequest, ActivityRoleResponse>
{
    public partial ActivityRoleResponse ToResponse(ActivityRole entity);
    public partial ActivityRole ToEntity(NameTextColorIconRequest request, long userId);

    public partial void UpdateEntity(NameTextColorIconRequest request, ActivityRole entity);

    [MapProperty(nameof(ActivityRole.Name), nameof(SelectOptionResponse.Text))]
    public partial SelectOptionResponse ToSelectOptionResponse(ActivityRole entity);

    public partial IQueryable<ActivityRoleResponse> ProjectToResponse(IQueryable<ActivityRole> source);
}
