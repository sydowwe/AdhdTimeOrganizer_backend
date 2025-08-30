using AdhdTimeOrganizer.application.dto.request;
using AdhdTimeOrganizer.application.dto.response.activityHistory;
using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.infrastructure.persistence;
using AlarmMapper = AdhdTimeOrganizer.application.mapper.AlarmMapper;

namespace AdhdTimeOrganizer.application.endpoint.activityHistory.alarm.command;

public class AlarmCreateEndpoint(AppCommandDbContext dbContext, AlarmMapper mapper)
    : BaseCreateEndpoint<Alarm, AlarmRequest, AlarmResponse, AlarmMapper>(dbContext, mapper);
