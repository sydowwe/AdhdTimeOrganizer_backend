using AdhdTimeOrganizer.application.dto.request.plannerTask;
using AdhdTimeOrganizer.application.dto.response.activityPlanning;
using AdhdTimeOrganizer.application.dto.response.generic;
using AdhdTimeOrganizer.application.mapper.@interface;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using Riok.Mapperly.Abstractions;

namespace AdhdTimeOrganizer.application.mapper.activityPlanning;

[Mapper]
public partial class PlannerTaskMapper : IBaseSimpleCrudMapper<PlannerTask, PlannerTaskRequest, PlannerTaskResponse>
{
    public partial PlannerTaskResponse ToResponse(PlannerTask entity);
    public partial SelectOptionResponse ToSelectOptionResponse(PlannerTask entity);
    public partial PlannerTask ToEntity(PlannerTaskRequest request, long userId);

    public partial void UpdateEntity(PlannerTaskRequest request, PlannerTask entity);
    public partial IQueryable<PlannerTaskResponse> ProjectToResponse(IQueryable<PlannerTask> source);

    private string MapStatus(TaskStatus status) => status.ToString();
}
