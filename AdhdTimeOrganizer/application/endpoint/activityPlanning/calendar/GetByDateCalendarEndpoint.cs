using System.Linq.Expressions;
using AdhdTimeOrganizer.application.dto.response.taskPlanner;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;
using Microsoft.EntityFrameworkCore;
using CalendarMapper = AdhdTimeOrganizer.application.mapper.activityPlanning.CalendarMapper;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.calendar;

public class GetByDateCalendarEndpoint(AppCommandDbContext dbContext, CalendarMapper mapper) : BaseGetByFieldEndpoint<Calendar, CalendarResponse, CalendarMapper>(dbContext, mapper)
{
    protected override string FieldName => nameof(Calendar.Date);
    protected override Expression<Func<Calendar, bool>> FilterQuery(string value)
    {
        return c => c.Date == DateOnly.ParseExact(value, "dd-MM-yyyy");
    }
    protected override IQueryable<Calendar> WithIncludes(IQueryable<Calendar> query)
    {
        return query.Include(t => t.Tasks);
    }
}