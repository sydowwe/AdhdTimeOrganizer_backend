using AdhdTimeOrganizer.Planning.application.dto.request.taskPlanner;
using AdhdTimeOrganizer.Planning.application.helper;
using AdhdTimeOrganizer.Planning.application.validator;
using AdhdTimeOrganizer.Planning.domain.model.entity;
using FastEndpoints;
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

    /// <summary>
    /// The rule itself is <see cref="CalendarDayTypeRule"/>, shared with the batched day-type endpoint, which
    /// turns the same violation into one day's <c>succeeded: false</c> instead of a 400.
    /// <para>
    /// The response is written here rather than by <c>ThrowError</c>, which is what this used to call:
    /// <c>BaseUpdateEndpoint.HandleAsync</c> wraps its whole body in a blanket <c>catch (Exception)</c>, so the
    /// <c>ValidationFailureException</c> that was meant to be a 400 came back out of <c>DbUtils.HandleException</c>
    /// as a <b>500</b> carrying the rule's message next to "an unexpected error occurred". Returning
    /// <c>false</c> after sending is the base's own "the response is already written" contract, and it returns
    /// before the save, so the rejected day type is not persisted.
    /// </para>
    /// </summary>
    protected override async Task<bool> AfterMapping(Calendar entity, CalendarRequest req, CancellationToken ct = default)
    {
        if (CalendarDayTypeRule.Violation(entity, entity.DayType) is not { } violation)
            return true;

        AddError(violation);
        await Send.ErrorsAsync(400, ct);
        return false;
    }
}