using AdhdTimeOrganizer.application.dto.response;
using AdhdTimeOrganizer.application.dto.response.activity;
using AdhdTimeOrganizer.application.dto.response.generic;
using AdhdTimeOrganizer.domain.model.entity.activity;
using Riok.Mapperly.Abstractions;
using AdhdTimeOrganizer.application.mapper.@interface;

namespace AdhdTimeOrganizer.application.mapper.activity;

[Mapper]
public partial class CategoryMapper : IBaseReadMapper<ActivityCategory, CategoryResponse>
{
    public partial CategoryResponse ToResponse(ActivityCategory entity);
    public partial SelectOptionResponse ToSelectOptionResponse(ActivityCategory entity);
}
