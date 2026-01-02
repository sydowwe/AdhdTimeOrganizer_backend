using System.Linq.Expressions;
using AdhdTimeOrganizer.application.dto.response.activityPlanning;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.application.mapper;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.taskPlannerDayTemplate.query;

public class GetByDateCalendarEndpoint(AppCommandDbContext dbContext, CalendarMapper mapper) : BaseGetByFieldEndpoint<Calendar, CalendarResponse, CalendarMapper>(dbContext, mapper)
{
    protected override string FieldName => nameof(Calendar.Date);
    protected override Expression<Func<Calendar, bool>> FilterQuery(string value)
    {
        return c => c.Date == DateOnly.ParseExact(value, "dd-MM-yyyy");
    }
}