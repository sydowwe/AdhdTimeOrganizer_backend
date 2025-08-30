using AdhdTimeOrganizer.application.dto.request.plannerTask;
using AdhdTimeOrganizer.application.dto.response.activityPlanning;
using AdhdTimeOrganizer.application.dto.response.generic;
using AdhdTimeOrganizer.application.mapper.@interface;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using Riok.Mapperly.Abstractions;

namespace AdhdTimeOrganizer.application.mapper.activityPlanning;

[Mapper]
public partial class PlannerTaskMapper : IBaseCrudMapper<PlannerTask, PlannerTaskRequest, PlannerTaskResponse>
{
    public partial PlannerTaskResponse ToResponse(PlannerTask entity);
    public partial SelectOptionResponse ToSelectOptionResponse(PlannerTask entity);
    public partial PlannerTask ToEntity(PlannerTaskRequest request, long userId);

    public partial void UpdateEntity(PlannerTaskRequest request, PlannerTask entity);
}
