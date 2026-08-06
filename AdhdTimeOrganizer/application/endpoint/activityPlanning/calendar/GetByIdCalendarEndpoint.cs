using AdhdTimeOrganizer.application.dto.response.taskPlanner;
using AdhdTimeOrganizer.domain.model.entity;
using AdhdTimeOrganizer.infrastructure.persistence;
using Sydowwe.Framework.application.endpoint.@base.read;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.calendar;

public class GetByIdCalendarEndpoint(AppDbContext dbContext)
    : BaseGetByIdEndpoint<Calendar, CalendarResponse>(dbContext)
{
    // Scoped by AppDbContext's global IEntityWithUser query filter, so a foreign row never projects.
    protected override Task<bool> AuthorizeAsync(CalendarResponse entity, CancellationToken ct) => Task.FromResult(true);
}