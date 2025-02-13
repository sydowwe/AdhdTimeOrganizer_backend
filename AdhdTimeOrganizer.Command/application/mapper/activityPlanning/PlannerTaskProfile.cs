using AdhdTimeOrganizer.Command.application.dto.request.plannerTask;
using AdhdTimeOrganizer.Command.application.dto.response.activityPlanning;
using AdhdTimeOrganizer.Command.domain.model.entity.activityPlanning;
using AutoMapper;

namespace AdhdTimeOrganizer.Command.application.mapper.activityPlanning;

public class PlannerTaskProfile : Profile
{
    public PlannerTaskProfile()
    {
        CreateMap<PlannerTaskRequest, PlannerTask>();
        CreateMap<PlannerTask, PlannerTaskResponse>();
    }
}