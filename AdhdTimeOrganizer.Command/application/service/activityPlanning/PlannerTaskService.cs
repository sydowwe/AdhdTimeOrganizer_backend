using AdhdTimeOrganizer.Command.application.dto.request.plannerTask;
using AdhdTimeOrganizer.Command.application.dto.response.activityPlanning;
using AdhdTimeOrganizer.Command.application.@interface.activity;
using AdhdTimeOrganizer.Command.application.@interface.activityPlanning;
using AdhdTimeOrganizer.Command.application.@interface.users;
using AdhdTimeOrganizer.Command.application.service.@base;
using AdhdTimeOrganizer.Command.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Command.domain.repositoryContract.activityPlanning;
using AutoMapper;

namespace AdhdTimeOrganizer.Command.application.service.activityPlanning;

public class PlannerTaskService(IPlannerTaskRepository repository,IActivityService activityService, ILoggedUserService loggedUserService, IMapper mapper)
    : EntityWithIsDoneService<PlannerTask, PlannerTaskRequest, PlannerTaskResponse, IPlannerTaskRepository>(repository, activityService, loggedUserService, mapper), IPlannerTaskService
{
    public async Task<List<PlannerTaskResponse>> GetAllByDateAndHourSpan(PlannerFilterRequest request)
    {
        var endDate = request.FilterDate.AddSeconds(request.HourSpan * 3600);
        return await ProjectFromQueryToListAsync<PlannerTaskResponse>(repository.GetAllByDateAndHourSpan(loggedUserService.GetUserId, request.FilterDate, endDate));
    }
};