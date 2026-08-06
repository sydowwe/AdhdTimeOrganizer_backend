using AdhdTimeOrganizer.domain.model.entity.activity;
using AdhdTimeOrganizer.infrastructure.persistence;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.application.endpoint.activity.role.command;

public class DeleteActivityRoleEndpoint(AppDbContext dbContext)
    : BaseDeleteEndpoint<ActivityRole>(dbContext);