using AdhdTimeOrganizer.application.dto.request.user;
using AdhdTimeOrganizer.application.dto.response.user;
using AdhdTimeOrganizer.config.dependencyInjection;
using AdhdTimeOrganizer.domain.model.entity.user;
using Riok.Mapperly.Abstractions;

namespace AdhdTimeOrganizer.application.mapper.user;

[Mapper]
public partial class UserPlannerSettingsMapper : IMapperService
{
    public partial UserPlannerSettingsResponse ToResponse(UserPlannerSettings entity);

    public partial void UpdateEntity(UserPlannerSettingsRequest request, UserPlannerSettings entity);
}
