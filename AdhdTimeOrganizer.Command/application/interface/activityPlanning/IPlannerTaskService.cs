using AdhdTimeOrganizer.Command.application.dto.request.plannerTask;
using AdhdTimeOrganizer.Command.application.dto.response.activityPlanning;
using AdhdTimeOrganizer.Command.application.@interface.@base;
using AdhdTimeOrganizer.Command.application.service.@base;
using AdhdTimeOrganizer.Command.domain.model.entity.activityPlanning;

namespace AdhdTimeOrganizer.Command.application.@interface.activityPlanning;

public interface IPlannerTaskService : IEntityWithIsDoneService<PlannerTask, PlannerTaskRequest, PlannerTaskResponse>
{
    Task<List<PlannerTaskResponse>> GetAllByDateAndHourSpan(PlannerFilterRequest request);
}