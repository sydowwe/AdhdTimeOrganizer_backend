using AdhdTimeOrganizer.application.dto.filter;
using AdhdTimeOrganizer.application.dto.request.generic;
using AdhdTimeOrganizer.application.dto.response.taskPlanner;
using AdhdTimeOrganizer.application.endpoint.@base.read.pageFilterSort;
using AdhdTimeOrganizer.domain.model.entity;
using AdhdTimeOrganizer.infrastructure.persistence;

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