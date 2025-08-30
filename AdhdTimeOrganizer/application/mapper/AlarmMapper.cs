using AdhdTimeOrganizer.application.dto.request;
using AdhdTimeOrganizer.application.dto.response.activityHistory;
using AdhdTimeOrganizer.application.dto.response.generic;
using AdhdTimeOrganizer.application.mapper.@interface;
using AdhdTimeOrganizer.domain.model.entity.activityHistory;
using Riok.Mapperly.Abstractions;

namespace AdhdTimeOrganizer.application.mapper;

[Mapper]
public partial class AlarmMapper : IBaseCrudMapper<Alarm, AlarmRequest, AlarmResponse>
{
    [MapProperty(nameof(Alarm.Activity.Name), nameof(AlarmResponse.Name))]
    [MapProperty(nameof(Alarm.Activity.Text), nameof(AlarmResponse.Text))]
    [MapProperty("Activity.Role.Color", nameof(AlarmResponse.Color))]
    public partial AlarmResponse ToResponse(Alarm entity);
    public partial SelectOptionResponse ToSelectOptionResponse(Alarm entity);
    public partial Alarm ToEntity(AlarmRequest request, long userId);

    public partial void UpdateEntity(AlarmRequest request, Alarm entity);
}
