using AdhdTimeOrganizer.application.dto.dto;
using AdhdTimeOrganizer.application.dto.request.taskPlanner.template;
using AdhdTimeOrganizer.application.dto.response.generic;
using AdhdTimeOrganizer.application.dto.response.taskPlanner.template;
using AdhdTimeOrganizer.application.mapper.@interface;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using Riok.Mapperly.Abstractions;

namespace AdhdTimeOrganizer.application.mapper.activityPlanning;

[Mapper]
public partial class TemplatePlannerTaskMapper : IBaseCrudMapper<TemplatePlannerTask, TemplatePlannerTaskRequest, TemplatePlannerTaskRequest, TemplatePlannerTaskResponse>
{
    public partial TemplatePlannerTaskResponse ToResponse(TemplatePlannerTask entity);
    public partial SelectOptionResponse ToSelectOptionResponse(TemplatePlannerTask entity);

    public partial void UpdateEntity(TemplatePlannerTaskRequest request, TemplatePlannerTask entity);

    public partial TemplatePlannerTask ToEntity(TemplatePlannerTaskRequest request, long userId);
    public partial IQueryable<TemplatePlannerTaskResponse> ProjectToResponse(IQueryable<TemplatePlannerTask> source);
    private static TimeDto MapTimeOnlyToTimeDto(TimeOnly timeOnly) => new() { Hours = timeOnly.Hour, Minutes = timeOnly.Minute };
    private static TimeOnly MapTimeDtoToTimeOnly(TimeDto timeDto) => new(timeDto.Hours, timeDto.Minutes);
}
