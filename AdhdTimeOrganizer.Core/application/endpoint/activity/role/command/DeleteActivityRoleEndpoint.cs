using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using Sydowwe.Framework.application.endpoint.@base.command;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.Core.application.endpoint.activity.role.command;

public class DeleteActivityRoleEndpoint(DbContext dbContext)
    : BaseDeleteEndpoint<ActivityRole>(dbContext);