using AdhdTimeOrganizer.application.dto.response.taskPlanner;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.application.helper;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;
using Microsoft.EntityFrameworkCore;
using CalendarMapper = AdhdTimeOrganizer.application.mapper.activityPlanning.CalendarMapper;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.calendar;

public class GetByIdCalendarEndpoint(AppCommandDbContext dbContext, CalendarMapper mapper)
    : BaseGetByIdEndpoint<Calendar, CalendarResponse, CalendarMapper>(dbContext, mapper)
{
    protected override IQueryable<Calendar> WithIncludes(IQueryable<Calendar> query)
    {
        return query.Include(t => t.Tasks);
    }
}