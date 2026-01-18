using AdhdTimeOrganizer.application.dto.request.toDoList;
using AdhdTimeOrganizer.application.dto.response.generic;
using AdhdTimeOrganizer.application.dto.response.taskPlanner;
using AdhdTimeOrganizer.application.mapper.@interface;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using Riok.Mapperly.Abstractions;

namespace AdhdTimeOrganizer.application.mapper.activityPlanning;

[Mapper]
public partial class TaskPriorityMapper : IBaseSimpleCrudMapper<TaskPriority, TaskPriorityRequest, TaskPriorityResponse>
{
    public partial TaskPriorityResponse ToResponse(TaskPriority entity);
    public partial SelectOptionResponse ToSelectOptionResponse(TaskPriority entity);
    public TaskPriority ToEntity(TaskPriorityRequest request, long userId)
    {
        throw new NotImplementedException();
    }

    public void UpdateEntity(TaskPriorityRequest request, TaskPriority entity)
    {
        throw new NotImplementedException();
    }

    public partial IQueryable<TaskPriorityResponse> ProjectToResponse(IQueryable<TaskPriority> query);

}
