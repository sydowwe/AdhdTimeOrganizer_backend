using AdhdTimeOrganizer.Planning.application.dto.response.taskPlanner;
using AdhdTimeOrganizer.Planning.domain.model.entity;
using Sydowwe.Framework.application.endpoint.@base.read;

namespace AdhdTimeOrganizer.Planning.application.endpoint.activityPlanning.calendar;

public class GetByIdCalendarEndpoint(DbContext dbContext)
    : BaseGetByIdEndpoint<Calendar, CalendarResponse>(dbContext)
{
    // Scoped by AppDbContext's global IEntityWithUser query filter, so a foreign row never projects.
    protected override Task<bool> AuthorizeAsync(CalendarResponse entity, CancellationToken ct) => Task.FromResult(true);
}