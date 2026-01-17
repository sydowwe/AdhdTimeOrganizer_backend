using AdhdTimeOrganizer.application.dto.dto;
using AdhdTimeOrganizer.application.dto.request.taskPlanner.template;
using AdhdTimeOrganizer.application.dto.response.activityPlanning;
using AdhdTimeOrganizer.application.dto.response.activityPlanning.taskPlannerDayTemplate;
using AdhdTimeOrganizer.application.dto.response.generic;
using AdhdTimeOrganizer.application.endpoint.activityPlanning.templateTask.command;
using AdhdTimeOrganizer.application.mapper.@interface;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using Riok.Mapperly.Abstractions;

namespace AdhdTimeOrganizer.application.mapper;

[Mapper]
public partial class CalendarMapper : IBaseReadMapper<Calendar, CalendarResponse>, IBaseUpdateMapper<Calendar,CalendarRequest>
{
    public partial CalendarResponse ToResponse(Calendar entity);
    public partial SelectOptionResponse ToSelectOptionResponse(Calendar entity);
    public partial void UpdateEntity(CalendarRequest request, Calendar entity);
    public partial IQueryable<CalendarResponse> ProjectToResponse(IQueryable<Calendar> source);


    private static TimeDto MapTimeOnlyToTimeDto(TimeOnly timeOnly) => new TimeDto { Hours = timeOnly.Hour, Minutes = timeOnly.Minute };
    private static TimeOnly MapTimeDtoToTimeOnly(TimeDto timeDto) => new TimeOnly(timeDto.Hours, timeDto.Minutes);
}
