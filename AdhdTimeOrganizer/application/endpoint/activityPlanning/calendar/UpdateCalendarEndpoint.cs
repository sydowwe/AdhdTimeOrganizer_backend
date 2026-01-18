using AdhdTimeOrganizer.application.dto.request.taskPlanner;
using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.domain.model.@enum;
using AdhdTimeOrganizer.infrastructure.persistence;
using CalendarMapper = AdhdTimeOrganizer.application.mapper.activityPlanning.CalendarMapper;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.calendar;

public class UpdateCalendarEndpoint(AppCommandDbContext dbContext, CalendarMapper mapper)
    : BaseUpdateEndpoint<Calendar, CalendarRequest, CalendarMapper>(dbContext, mapper)
{
    protected override Task AfterMapping(Calendar entity, CalendarRequest req, CancellationToken ct = default)
    {
        ValidateDayType(entity);
        return Task.CompletedTask;
    }

    private void ValidateDayType(Calendar entity)
    {
        switch (entity.DayType)
        {
            case DayType.Holiday when string.IsNullOrWhiteSpace(entity.HolidayName):
                ThrowError("DayType 'Holiday' requires HolidayName to be set");
                break;
            case DayType.Weekend when !entity.IsWeekend:
                ThrowError($"DayType 'Weekend' is only valid on Saturday or Sunday, but date {entity.Date} is a {entity.Date.DayOfWeek}");
                break;
            case DayType.Workday when entity.IsWeekend:
                ThrowError($"DayType 'Workday' is not valid on weekends, but date {entity.Date} is a {entity.Date.DayOfWeek}");
                break;
        }
    }
}
