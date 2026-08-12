using AdhdTimeOrganizer.Core.domain.model.@enum;
using AdhdTimeOrganizer.Planning.application.dto.request.taskPlanner;
using AdhdTimeOrganizer.Planning.application.validator;
using AdhdTimeOrganizer.Planning.domain.model.entity;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.Planning.application.endpoint.activityPlanning.calendar;

public class UpdateCalendarEndpoint(DbContext dbContext)
    : BaseUpdateEndpoint<Calendar, CalendarRequest>(dbContext)
{
    public override void Configure()
    {
        base.Configure();
        Validator<CalendarRequestValidator>();
    }

    protected override Task<bool> AfterMapping(Calendar entity, CalendarRequest req, CancellationToken ct = default)
    {
        // ValidateDayType throws (ThrowError) on invalid input, so reaching here means the update is
        // valid and must be saved — returning false would silently discard it and send no response.
        ValidateDayType(entity);
        return Task.FromResult(true);
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