using AdhdTimeOrganizer.application.dto.request.taskPlanner;
using AdhdTimeOrganizer.application.dto.response.generic;
using AdhdTimeOrganizer.application.dto.response.taskPlanner;
using AdhdTimeOrganizer.application.mapper.@interface;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using Riok.Mapperly.Abstractions;

namespace AdhdTimeOrganizer.application.mapper.activityPlanning;

[Mapper]
public partial class TaskImportanceMapper : IBaseSimpleCrudMapper<TaskImportance, TaskImportanceRequest, TaskImportanceResponse>
{
    public partial TaskImportanceResponse ToResponse(TaskImportance entity);
    public partial SelectOptionResponse ToSelectOptionResponse(TaskImportance entity);
    public TaskImportance ToEntity(TaskImportanceRequest request, long userId)
    {
        throw new NotImplementedException();
    }

    public void UpdateEntity(TaskImportanceRequest request, TaskImportance entity)
    {
        throw new NotImplementedException();
    }

    public partial IQueryable<TaskImportanceResponse> ProjectToResponse(IQueryable<TaskImportance> query);

}
