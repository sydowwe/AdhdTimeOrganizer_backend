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

    public partial void UpdateEntity(ActivityHistoryRequest request, ActivityHistory entity);

    [MapperIgnoreTarget(nameof(ActivityHistory.EndTimestamp))]
    private partial ActivityHistory ToEntityBase(ActivityHistoryRequest request, long userId);

    public ActivityHistory ToEntity(ActivityHistoryRequest request, long userId)
    {
        var entity = ToEntityBase(request, userId);
        entity.EndTimestamp = request.StartTimestamp.AddSeconds(request.Length.TotalSeconds);
        return entity;
    }
}
