using AdhdTimeOrganizer.application.dto.response.activityPlanning;
using AdhdTimeOrganizer.application.dto.response.generic;
using AdhdTimeOrganizer.application.mapper.@interface;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using Riok.Mapperly.Abstractions;

namespace AdhdTimeOrganizer.application.mapper.activityPlanning;

[Mapper]
public partial class PlannerTaskMapper : IBaseReadMapper<PlannerTask, PlannerTaskResponse>
{
    public partial PlannerTaskResponse ToResponse(PlannerTask entity);
    public partial SelectOptionResponse ToSelectOptionResponse(PlannerTask entity);
}
