using AdhdTimeOrganizer.application.dto.dto;
using AdhdTimeOrganizer.application.dto.request.taskPlanner;
using AdhdTimeOrganizer.application.dto.response.generic;
using AdhdTimeOrganizer.application.dto.response.taskPlanner;
using AdhdTimeOrganizer.application.mapper.@interface;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.domain.model.@enum;
using Riok.Mapperly.Abstractions;

namespace AdhdTimeOrganizer.application.mapper.activityPlanning;

[Mapper]
public partial class CalendarMapper : IBaseReadMapper<Calendar, CalendarResponse>, IBaseUpdateMapper<Calendar,CalendarRequest>
{
    public partial CalendarResponse ToResponse(Calendar entity);
    public partial SelectOptionResponse ToSelectOptionResponse(Calendar entity);
    public partial void UpdateEntity(CalendarRequest request, Calendar entity);

    public IQueryable<CalendarResponse> ProjectToResponse(IQueryable<Calendar> source) =>
        source.Select(c => new CalendarResponse
        {
            Id = c.Id,
            Date = c.Date,
            DayIndex = c.Date.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)c.Date.DayOfWeek,
            DayType = c.DayType,
            HolidayName = c.HolidayName,
            Label = c.Label,
            WakeUpTime = new TimeDto(c.WakeUpTime.Hour, c.WakeUpTime.Minute),
            BedTime = new TimeDto(c.BedTime.Hour, c.BedTime.Minute),
            AppliedTemplateId = c.AppliedTemplateId,
            AppliedTemplateName = c.AppliedTemplateName,
            Location = c.Location,
            Weather = c.Weather,
            Notes = c.Notes,
            TotalTasks = c.Tasks.Count(),
            CompletedTasks = c.Tasks.Count(t => t.Status == PlannerTaskStatus.Completed),
        });

    private static TimeDto MapTimeOnlyToTimeDto(TimeOnly timeOnly) => new(timeOnly.Hour, timeOnly.Minute);
    private static TimeOnly MapTimeDtoToTimeOnly(TimeDto timeDto) => new(timeDto.Hours, timeDto.Minutes);
}
