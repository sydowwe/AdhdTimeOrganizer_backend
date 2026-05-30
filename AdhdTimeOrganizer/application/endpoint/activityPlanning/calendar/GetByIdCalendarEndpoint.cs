using AdhdTimeOrganizer.application.dto.response.taskPlanner;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.domain.model.entity;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.calendar;

public class GetByIdCalendarEndpoint(AppDbContext dbContext)
    : BaseGetByIdEndpoint<Calendar, CalendarResponse>(dbContext)
{
}