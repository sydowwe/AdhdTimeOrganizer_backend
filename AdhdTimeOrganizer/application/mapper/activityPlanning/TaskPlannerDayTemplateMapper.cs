using AdhdTimeOrganizer.application.dto.dto;
using AdhdTimeOrganizer.application.dto.request.taskPlanner.template;
using AdhdTimeOrganizer.application.dto.response.generic;
using AdhdTimeOrganizer.application.dto.response.taskPlanner.template;
using AdhdTimeOrganizer.application.mapper.@interface;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using Riok.Mapperly.Abstractions;

namespace AdhdTimeOrganizer.application.mapper.activityPlanning;

[Mapper]
public partial class TaskPlannerDayTemplateMapper : IBaseSimpleCrudMapper<TaskPlannerDayTemplate, TaskPlannerDayTemplateRequest, TaskPlannerDayTemplateResponse>, IBaseSelectOptionMapper<TaskPlannerDayTemplate>
{
    public partial TaskPlannerDayTemplateResponse ToResponse(TaskPlannerDayTemplate entity);
    [MapProperty(nameof(entity.Name), nameof(SelectOptionResponse.Text))]
    public partial SelectOptionResponse ToSelectOptionResponse(TaskPlannerDayTemplate entity);

    public partial void UpdateEntity(TaskPlannerDayTemplateRequest request, TaskPlannerDayTemplate entity);

    [MapperIgnoreTarget(nameof(TaskPlannerDayTemplate.UsageCount))]
    [MapperIgnoreTarget(nameof(TaskPlannerDayTemplate.LastUsedAt))]
    public partial TaskPlannerDayTemplate ToEntity(TaskPlannerDayTemplateRequest request, long userId);

    public partial IQueryable<TaskPlannerDayTemplateResponse> ProjectToResponse(IQueryable<TaskPlannerDayTemplate> source);
    private static TimeDto MapTimeOnlyToTimeDto(TimeOnly timeOnly) => new(timeOnly.Hour, timeOnly.Minute);
    private static TimeOnly MapTimeDtoToTimeOnly(TimeDto timeDto) => new(timeDto.Hours, timeDto.Minutes);
}
