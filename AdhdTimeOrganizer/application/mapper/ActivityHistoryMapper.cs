using AdhdTimeOrganizer.application.dto.response.activityHistory;
using AdhdTimeOrganizer.application.dto.response.generic;
using AdhdTimeOrganizer.application.mapper.@interface;
using AdhdTimeOrganizer.domain.model.entity.activityHistory;
using Riok.Mapperly.Abstractions;

namespace AdhdTimeOrganizer.application.mapper.activityHistory;

[Mapper]
public partial class ActivityHistoryMapper : IBaseReadMapper<ActivityHistory, ActivityHistoryResponse>
{
    public partial ActivityHistoryResponse ToResponse(ActivityHistory entity);
    public partial SelectOptionResponse ToSelectOptionResponse(ActivityHistory entity);
}
