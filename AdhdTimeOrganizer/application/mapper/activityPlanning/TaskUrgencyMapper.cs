using AdhdTimeOrganizer.application.dto.response.activityPlanning;
using AdhdTimeOrganizer.application.dto.response.generic;
using AdhdTimeOrganizer.application.mapper.@interface;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using Riok.Mapperly.Abstractions;

namespace AdhdTimeOrganizer.application.mapper.activityPlanning;

[Mapper]
public partial class TaskUrgencyMapper : IBaseReadMapper<TaskUrgency, TaskUrgencyResponse>
{
    public partial TaskUrgencyResponse ToResponse(TaskUrgency entity);
    public partial SelectOptionResponse ToSelectOptionResponse(TaskUrgency entity);
}
