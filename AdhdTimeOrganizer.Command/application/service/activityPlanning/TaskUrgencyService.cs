using AdhdTimeOrganizer.Command.application.dto.request.toDoList;
using AdhdTimeOrganizer.Command.application.dto.response.activityPlanning;
using AdhdTimeOrganizer.Command.application.@interface.activityPlanning;
using AdhdTimeOrganizer.Command.application.@interface.users;
using AdhdTimeOrganizer.Command.application.service.@base;
using AdhdTimeOrganizer.Command.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Command.domain.repositoryContract.activityPlanning;
using AutoMapper;

namespace AdhdTimeOrganizer.Command.application.service.activityPlanning;

public class TaskUrgencyService(ITaskUrgencyRepository repository, ILoggedUserService loggedUserService, IMapper mapper)
    : BaseCrudServiceWithUser<TaskUrgency, TaskUrgencyRequest, TaskUrgencyResponse, ITaskUrgencyRepository>(repository, loggedUserService, mapper), ITaskUrgencyService
{
    public async Task CreateDefaultItems(long newUserId)
    {
        await _repository.AddRangeAsync(
            [
                new TaskUrgency{UserId = newUserId,Text = "Today", Color ="#FF5252", Priority = 1},         // Red
                new TaskUrgency{UserId = newUserId,Text = "This week",Color = "#FFA726", Priority = 2},      // Orange
                new TaskUrgency{UserId = newUserId,Text = "This month",Color = "#FFD600", Priority = 3},     // Yellow
                new TaskUrgency{UserId = newUserId,Text = "This year",Color = "#4CAF50", Priority = 4}
            ]
        );
    }
};