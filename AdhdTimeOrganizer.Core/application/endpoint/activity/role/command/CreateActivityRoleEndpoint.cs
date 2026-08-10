using AdhdTimeOrganizer.Core.application.dto.request.activity;
using AdhdTimeOrganizer.Core.application.validator;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using Sydowwe.Framework.application.endpoint.@base.command;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.Core.application.endpoint.activity.role.command;

public class CreateActivityRoleEndpoint(DbContext dbContext)
    : BaseCreateEndpoint<ActivityRole, ActivityRoleRequest>(dbContext)
{
    public override void Configure()
    {
        base.Configure();
        Validator<ActivityRoleValidator>();
    }
}