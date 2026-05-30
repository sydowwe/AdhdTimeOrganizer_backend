using AdhdTimeOrganizer.application.dto.request.taskPlanner;
using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.application.validator;
using AdhdTimeOrganizer.domain.model.entity;
using AdhdTimeOrganizer.domain.model.@enum;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.calendar;

public class UpdateCalendarEndpoint(AppDbContext dbContext)
    : BaseUpdateEndpoint<Calendar, CalendarRequest>(dbContext)
{
    public override void Configure()
    {
        base.Configure();
        Validator<CalendarRequestValidator>();
    }

    protected override Task<bool> AfterMapping(Calendar entity, CalendarRequest req, CancellationToken ct = default)
    {
        ValidateDayType(entity);
        return Task.FromResult(false);
    }

    private void ValidateDayType(Calendar entity)
    {
        switch (entity.DayType)
        {
            case DayType.Weekend when !entity.IsWeekend:
                ThrowError($"DayType 'Weekend' is only valid on Saturday or Sunday, but date {entity.Date} is a {entity.Date.DayOfWeek}");
                break;
            case DayType.Workday when entity.IsWeekend:
                ThrowError($"DayType 'Workday' is not valid on weekends, but date {entity.Date} is a {entity.Date.DayOfWeek}");
                break;
        }
    }
}
