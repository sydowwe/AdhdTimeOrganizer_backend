using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.Core.application.endpoint.activity.role.command;

public class DeleteActivityRoleEndpoint(DbContext dbContext)
    : BaseDeleteEndpoint<ActivityRole>(dbContext);