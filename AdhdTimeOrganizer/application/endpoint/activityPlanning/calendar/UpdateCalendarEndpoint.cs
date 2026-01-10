using AdhdTimeOrganizer.application.dto.request.plannerTask;
using AdhdTimeOrganizer.application.dto.request.taskPlanner;
using AdhdTimeOrganizer.application.dto.response.activityPlanning;
using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.application.mapper;
using AdhdTimeOrganizer.application.mapper.activityPlanning;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.taskPlanner.command;

public class UpdateCalendarEndpoint(AppCommandDbContext dbContext, CalendarMapper mapper)
    : BaseUpdateEndpoint<Calendar, CalendarRequest, CalendarMapper>(dbContext, mapper);
