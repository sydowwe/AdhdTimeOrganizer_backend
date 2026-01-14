using System.Linq.Expressions;
using AdhdTimeOrganizer.application.dto.response.activityPlanning;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.application.mapper;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.taskPlannerDayTemplate.query;

public class GetByIdCalendarEndpoint(AppCommandDbContext dbContext, CalendarMapper mapper)
    : BaseGetByIdEndpoint<Calendar, CalendarResponse, CalendarMapper>(dbContext, mapper);