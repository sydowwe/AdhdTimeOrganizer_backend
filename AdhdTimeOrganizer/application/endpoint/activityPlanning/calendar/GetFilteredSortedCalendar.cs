using AdhdTimeOrganizer.Core.application.dto.filter;
using AdhdTimeOrganizer.application.dto.filter;
using AdhdTimeOrganizer.application.dto.response.taskPlanner;
using AdhdTimeOrganizer.domain.model.entity;
using AdhdTimeOrganizer.infrastructure.persistence;
using Sydowwe.Framework.application.dto.request.generic;
using Sydowwe.Framework.application.endpoint.@base.read.pageFilterSort;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.calendar;

public class FilterCalendarEndpoint(AppDbContext dbContext)
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