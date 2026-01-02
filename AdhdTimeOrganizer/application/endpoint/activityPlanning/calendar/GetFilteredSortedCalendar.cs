using AdhdTimeOrganizer.application.dto.filter;
using AdhdTimeOrganizer.application.dto.request.generic;
using AdhdTimeOrganizer.application.dto.response.activityPlanning;
using AdhdTimeOrganizer.application.dto.response.activityPlanning.taskPlannerDayTemplate;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.application.mapper;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.taskPlannerDayTemplate.query;

public class GetFilteredSortedCalendar(AppCommandDbContext dbContext, CalendarMapper mapper)
    : BaseFilterEndpoint<Calendar, CalendarResponse, CalendarFilter, CalendarMapper>(dbContext, mapper)
{
    protected override IQueryable<Calendar> ApplyCustomFiltering(IQueryable<Calendar> query, CalendarFilter filter)
    {
        return query.Where(c => c.Date >= filter.From && c.Date <= filter.Until);
    }

    public override SortByRequest[] DefaultSortBy =>
    [
        new()
        {
            Key = "Date",
            IsDesc = false
        }
    ];

    protected override IQueryable<Calendar> WithIncludes(IQueryable<Calendar> query)
    {
        return query.Include(t => t.Tasks);
    }
}