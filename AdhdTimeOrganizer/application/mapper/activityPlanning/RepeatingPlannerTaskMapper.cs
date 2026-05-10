using AdhdTimeOrganizer.application.dto.dto;
using AdhdTimeOrganizer.application.dto.request.taskPlanner;
using AdhdTimeOrganizer.application.dto.response.taskPlanner;
using AdhdTimeOrganizer.application.mapper.@interface;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using Riok.Mapperly.Abstractions;

namespace AdhdTimeOrganizer.application.mapper.activityPlanning;

[Mapper]
public partial class RepeatingPlannerTaskMapper : IBaseCrudMapper<RepeatingPlannerTask, RepeatingPlannerTaskRequest, RepeatingPlannerTaskRequest, RepeatingPlannerTaskResponse>
{
    public partial RepeatingPlannerTaskResponse ToResponse(RepeatingPlannerTask entity);

    public partial void UpdateEntity(RepeatingPlannerTaskRequest request, RepeatingPlannerTask entity);

    public partial RepeatingPlannerTask ToEntity(RepeatingPlannerTaskRequest request, long userId);

    public partial IQueryable<RepeatingPlannerTaskResponse> ProjectToResponse(IQueryable<RepeatingPlannerTask> source);

    private static TimeDto MapTimeOnlyToTimeDto(TimeOnly timeOnly) => new(timeOnly.Hour, timeOnly.Minute);

    private static TimeOnly MapTimeDtoToTimeOnly(TimeDto timeDto) => new(timeDto.Hours, timeDto.Minutes);
}
