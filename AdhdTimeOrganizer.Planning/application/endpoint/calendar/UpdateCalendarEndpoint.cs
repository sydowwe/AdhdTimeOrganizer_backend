using AdhdTimeOrganizer.Planning.application.dto.request.taskPlanner;
using AdhdTimeOrganizer.Planning.application.helper;
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

    /// <summary>
    /// The rule itself is <see cref="CalendarDayTypeRule"/>, shared with the batched day-type endpoint, which
    /// turns the same violation into one day's <c>succeeded: false</c> instead of a 400.
    /// </summary>
    private void ValidateDayType(Calendar entity)
    {
        if (CalendarDayTypeRule.Violation(entity, entity.DayType) is { } violation)
            ThrowError(violation);
    }
}