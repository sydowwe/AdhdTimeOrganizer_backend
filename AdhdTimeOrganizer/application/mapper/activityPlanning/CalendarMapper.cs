using AdhdTimeOrganizer.application.dto.dto;
using AdhdTimeOrganizer.application.dto.request.taskPlanner;
using AdhdTimeOrganizer.application.dto.response.generic;
using AdhdTimeOrganizer.application.dto.response.taskPlanner;
using AdhdTimeOrganizer.application.mapper.@interface;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using Riok.Mapperly.Abstractions;

namespace AdhdTimeOrganizer.application.mapper.activityPlanning;

[Mapper]
public partial class CalendarMapper : IBaseReadMapper<Calendar, CalendarResponse>, IBaseUpdateMapper<Calendar,CalendarRequest>
{
    public partial CalendarResponse ToResponse(Calendar entity);
    public partial SelectOptionResponse ToSelectOptionResponse(Calendar entity);
    public partial void UpdateEntity(CalendarRequest request, Calendar entity);
    public partial IQueryable<CalendarResponse> ProjectToResponse(IQueryable<Calendar> source);


    private static TimeDto MapTimeOnlyToTimeDto(TimeOnly timeOnly) => new() { Hours = timeOnly.Hour, Minutes = timeOnly.Minute };
    private static TimeOnly MapTimeDtoToTimeOnly(TimeDto timeDto) => new(timeDto.Hours, timeDto.Minutes);
}
