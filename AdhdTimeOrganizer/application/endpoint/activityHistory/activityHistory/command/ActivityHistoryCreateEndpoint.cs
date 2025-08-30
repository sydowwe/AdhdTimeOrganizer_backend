using AdhdTimeOrganizer.application.dto.request.history;
using AdhdTimeOrganizer.application.dto.response.activityHistory;
using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.infrastructure.persistence;
using ActivityHistoryMapper = AdhdTimeOrganizer.application.mapper.ActivityHistoryMapper;

namespace AdhdTimeOrganizer.application.endpoint.activityHistory.activityHistory.command;

public class ActivityHistoryCreateEndpoint(AppCommandDbContext dbContext, ActivityHistoryMapper mapper)
    : BaseCreateEndpoint<ActivityHistory, ActivityHistoryRequest, ActivityHistoryResponse, ActivityHistoryMapper>(dbContext, mapper);
