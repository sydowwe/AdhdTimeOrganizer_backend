using AdhdTimeOrganizer.application.dto.request.history;
using AdhdTimeOrganizer.application.dto.response.activityHistory;
using AdhdTimeOrganizer.application.dto.response.generic;
using AdhdTimeOrganizer.application.mapper.@interface;
using AdhdTimeOrganizer.domain.model.entity.activityHistory;
using Riok.Mapperly.Abstractions;

namespace AdhdTimeOrganizer.application.mapper;

[Mapper]
public partial class ActivityHistoryMapper : IBaseCrudMapper<ActivityHistory, ActivityHistoryRequest, ActivityHistoryResponse>
{
    public partial ActivityHistoryResponse ToResponse(ActivityHistory entity);
    public partial SelectOptionResponse ToSelectOptionResponse(ActivityHistory entity);
    public partial ActivityHistory ToEntity(ActivityHistoryRequest request, long userId);

    public partial void UpdateEntity(ActivityHistoryRequest request, ActivityHistory entity);
}
