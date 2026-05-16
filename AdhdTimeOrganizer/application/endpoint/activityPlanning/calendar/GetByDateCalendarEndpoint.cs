using System.Linq.Expressions;
using AdhdTimeOrganizer.application.dto.response.taskPlanner;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.domain.model.entity;
using AdhdTimeOrganizer.application.validator;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using CalendarMapper = AdhdTimeOrganizer.application.mapper.activityPlanning.CalendarMapper;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.calendar;

public class GetByDateCalendarEndpoint(AppDbContext dbContext, CalendarMapper mapper) : BaseGetByFieldEndpoint<Calendar, CalendarResponse, CalendarMapper>(dbContext, mapper)
{
    protected override string FieldName => nameof(Calendar.Date);

    protected override Expression<Func<Calendar, bool>> FilterQuery(string value)
    {
        if (DateOnly.TryParseExact(value, "dd-MM-yyyy", out var date))
            return c => c.Date == date;

        AddError(nameof(value), "Invalid date format");
        Send.ErrorsAsync();
        return c => false;
    }

    protected override IQueryable<Calendar> WithIncludes(IQueryable<Calendar> query)
    {
        return query.Include(t => t.Tasks);
    }
}