using AdhdTimeOrganizer.Planning.application.dto.filter;
using AdhdTimeOrganizer.Planning.application.dto.response.taskPlanner;
using AdhdTimeOrganizer.Planning.application.validator;
using AdhdTimeOrganizer.Planning.domain.model.entity;
using FastEndpoints;
using Sydowwe.Framework.application.dto.request.generic;
using Sydowwe.Framework.application.endpoint.@base.read.pageFilterSort;

namespace AdhdTimeOrganizer.Planning.application.endpoint.activityPlanning.calendar;

public class FilterCalendarEndpoint(DbContext dbContext)
    : BaseFilterEndpoint<Calendar, CalendarResponse, CalendarFilter>(dbContext)
{
    public override void Configure()
    {
        base.Configure();
        // The range ceiling — see CalendarFilterValidator. Wired explicitly rather than left to FastEndpoints'
        // convention binding, matching every other validator in the slice: convention binding resolves only
        // while there is exactly one validator for the request type, and stops silently when a second appears.
        Validator<CalendarFilterValidator>();
    }

    protected override IQueryable<Calendar> ApplyCustomFiltering(IQueryable<Calendar> query, CalendarFilter filter)
    {
        return query.Where(c => c.Date >= filter.From && c.Date <= filter.Until);
    }

    public override SortByRequest[] AlwaysSortBy =>
    [
        new()
        {
            Key = "Date",
            IsDesc = false
        }
    ];
}