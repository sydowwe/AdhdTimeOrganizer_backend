using AdhdTimeOrganizer.application.dto.request.toDoList;
using AdhdTimeOrganizer.application.dto.response.generic;
using AdhdTimeOrganizer.application.dto.response.taskPlanner;
using AdhdTimeOrganizer.application.mapper.@interface;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using Riok.Mapperly.Abstractions;

namespace AdhdTimeOrganizer.application.mapper.activityPlanning;

[Mapper]
public partial class TaskPriorityMapper : IBaseSimpleCrudMapper<TaskPriority, TaskPriorityRequest, TaskPriorityResponse>, IBaseSelectOptionMapper<TaskPriority>
{
    public partial TaskPriorityResponse ToResponse(TaskPriority entity);

    public partial TaskPriority ToEntity(TaskPriorityRequest request, long userId);

    public partial void UpdateEntity(TaskPriorityRequest request, TaskPriority entity);
    public partial IQueryable<TaskPriorityResponse> ProjectToResponse(IQueryable<TaskPriority> query);

    public SelectOptionResponse ToSelectOptionResponse(TaskPriority entity) => new(entity.Id, entity.Text);
}