using AdhdTimeOrganizer.application.dto.request.@base;
using AdhdTimeOrganizer.application.dto.response;
using AdhdTimeOrganizer.application.dto.response.generic;
using AdhdTimeOrganizer.domain.model.entity.activity;
using Riok.Mapperly.Abstractions;
using AdhdTimeOrganizer.application.mapper.@interface;

namespace AdhdTimeOrganizer.application.mapper.activity;

[Mapper]
public partial class ActivityCategoryMapper :  IBaseSimpleCrudMapper<ActivityCategory, NameTextColorIconRequest, ActivityCategoryResponse>, IBaseSelectOptionMapper<ActivityCategory>
{
    [MapperIgnoreTarget(nameof(ActivityCategoryResponse.Role))]
    private partial ActivityCategoryResponse ToResponseBase(ActivityCategory entity);

    public ActivityCategoryResponse ToResponse(ActivityCategory entity)
    {
        var response = ToResponseBase(entity);
        return response with { Role = GetRole(entity) };
    }

    public partial ActivityCategory ToEntity(NameTextColorIconRequest request, long userId);
    public partial void UpdateEntity(NameTextColorIconRequest request, ActivityCategory entity);

    [MapProperty(nameof(ActivityCategory.Name), nameof(SelectOptionResponse.Text))]
    public partial SelectOptionResponse ToSelectOptionResponse(ActivityCategory entity);

    [MapperIgnoreTarget(nameof(ActivityCategoryResponse.Role))]
    public partial IQueryable<ActivityCategoryResponse> ProjectToResponse(IQueryable<ActivityCategory> source);

    private string? GetRole(ActivityCategory entity) => entity.Activities.FirstOrDefault()?.Role?.Name;
}
