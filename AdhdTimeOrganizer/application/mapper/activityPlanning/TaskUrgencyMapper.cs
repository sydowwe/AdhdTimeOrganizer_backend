using AdhdTimeOrganizer.application.dto.request.toDoList;
using AdhdTimeOrganizer.application.dto.response.activityPlanning;
using AdhdTimeOrganizer.application.dto.response.generic;
using AdhdTimeOrganizer.application.mapper.@interface;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using Riok.Mapperly.Abstractions;

namespace AdhdTimeOrganizer.application.mapper.activityPlanning;

[Mapper]
public partial class TaskUrgencyMapper : IBaseSimpleCrudMapper<TaskUrgency, TaskUrgencyRequest, TaskUrgencyResponse>
{
    public partial TaskUrgencyResponse ToResponse(TaskUrgency entity);
    public partial SelectOptionResponse ToSelectOptionResponse(TaskUrgency entity);
    public TaskUrgency ToEntity(TaskUrgencyRequest request, long userId)
    {
        throw new NotImplementedException();
    }

    public void UpdateEntity(TaskUrgencyRequest request, TaskUrgency entity)
    {
        throw new NotImplementedException();
    }

    public partial IQueryable<TaskUrgencyResponse> ProjectToResponse(IQueryable<TaskUrgency> query);

}
