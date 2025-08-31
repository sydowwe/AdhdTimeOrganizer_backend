using AdhdTimeOrganizer.application.dto.request.@base;
using AdhdTimeOrganizer.application.dto.response;
using AdhdTimeOrganizer.application.dto.response.activity;
using AdhdTimeOrganizer.application.dto.response.generic;
using AdhdTimeOrganizer.domain.model.entity.activity;
using Riok.Mapperly.Abstractions;
using AdhdTimeOrganizer.application.mapper.@interface;

namespace AdhdTimeOrganizer.application.mapper.activity;

[Mapper]
public partial class ActivityCategoryMapper :  IBaseCrudMapper<ActivityCategory, NameTextColorIconRequest, ActivityCategoryResponse>
{
    public partial ActivityCategoryResponse ToResponse(ActivityCategory entity);
    public partial ActivityCategory ToEntity(NameTextColorIconRequest request, long userId);
    public partial void UpdateEntity(NameTextColorIconRequest request, ActivityCategory entity);

    [MapProperty(nameof(ActivityCategory.Name), nameof(SelectOptionResponse.Text))]
    public partial SelectOptionResponse ToSelectOptionResponse(ActivityCategory entity);
}
