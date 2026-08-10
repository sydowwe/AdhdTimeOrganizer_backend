using AdhdTimeOrganizer.Planning.application.dto.filter;
using AdhdTimeOrganizer.Planning.application.dto.response.taskPlanner;
using AdhdTimeOrganizer.Planning.domain.model.entity;
using Sydowwe.Framework.application.dto.request.generic;
using Sydowwe.Framework.application.endpoint.@base.read.pageFilterSort;

namespace AdhdTimeOrganizer.Planning.application.endpoint.activityPlanning.calendar;

public class FilterCalendarEndpoint(DbContext dbContext)
    : BaseFilterEndpoint<Calendar, CalendarResponse, CalendarFilter>(dbContext)
{
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