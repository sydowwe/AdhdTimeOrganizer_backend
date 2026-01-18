using AdhdTimeOrganizer.application.dto.request.activity;
using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.application.mapper.activity;
using AdhdTimeOrganizer.domain.model.entity.activity;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activity.activity.command;

public class UpdateActivityEndpoint(AppCommandDbContext dbContext, ActivityMapper mapper)
    : BaseUpdateEndpoint<Activity, ActivityRequest, ActivityMapper>(dbContext, mapper);
