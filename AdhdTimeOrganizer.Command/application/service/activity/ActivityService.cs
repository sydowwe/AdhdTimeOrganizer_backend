using AdhdTimeOrganizer.Command.application.dto.request.activity;
using AdhdTimeOrganizer.Command.application.dto.request.extendable;
using AdhdTimeOrganizer.Command.application.dto.response.activity;
using AdhdTimeOrganizer.Command.application.@interface.activity;
using AdhdTimeOrganizer.Command.application.@interface.users;
using AdhdTimeOrganizer.Command.application.service.@base;
using AdhdTimeOrganizer.Command.domain.@event;
using AdhdTimeOrganizer.Command.domain.model.entity.activity;
using AdhdTimeOrganizer.Command.domain.repositoryContract.activity;
using AdhdTimeOrganizer.Common.application.dto.request.@base;
using AdhdTimeOrganizer.Common.domain.result;
using AutoMapper;
using MediatR;

namespace AdhdTimeOrganizer.Command.application.service.activity;

public class ActivityService(IActivityRepository repository, ILoggedUserService loggedUserService, IMapper mapper, IMediator mediator)
    : BaseWithUserService<Activity, ActivityRequest, ActivityResponse, IActivityRepository>(repository, loggedUserService, mapper),
        IActivityService
{
    public new async Task<ServiceResult<ActivityResponse>> InsertAsync(ActivityRequest request)
    {
        var entity = mapper.Map<Activity>(request);
        var result = await base.InsertAsync(entity);
        if (result.Data.IsOnToDoList)
        {
            if (!request.ToDoListUrgencyId.HasValue)
            {
                return ServiceResult<ActivityResponse>.Error(ServiceErrorType.MissingArgument,"ToDoListUrgencyId is required when IsOnToDoList is true");
            }
            await mediator.Publish(new ActivityCreatedIsOnToDoListEvent(entity.Id, request.ToDoListUrgencyId.Value));
        }
        return result;
    }

    public async Task<List<ActivityFormSelectOptionsResponse>> GetAllFormSelectOptions()
    {
        return await ProjectFromQueryToListAsync<ActivityFormSelectOptionsResponse>(_repository.GetAllByUserIdAsQueryable(LoggedUserId).Distinct());
    }
    public async Task<ServiceResult<ActivityResponse>> QuickUpdateAsync(long id, NameTextRequest request)
    {
        var result = await _repository.GetByIdAsync(id);
        if (result.Failed)
        {
            return HandleFailedRepositoryResult<ActivityResponse>(result);
        }

        var entity = result.Data;
        mapper.Map(request, entity);
        await _repository.UpdateAsync(entity);
        return ServiceResult<ActivityResponse>.Successful(mapper.Map<ActivityResponse>(entity));
    }
}