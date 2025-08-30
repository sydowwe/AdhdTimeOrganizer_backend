using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.domain.model.entity.activity;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activity.role.command;

public class ActivityRoleDeleteEndpoint(AppCommandDbContext dbContext)
    : BaseDeleteEndpoint<ActivityRole>(dbContext);
