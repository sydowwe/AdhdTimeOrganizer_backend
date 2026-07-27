using AdhdTimeOrganizer.application.dto.request.activity;
using AdhdTimeOrganizer.application.validator;
using AdhdTimeOrganizer.domain.model.entity.activity;
using AdhdTimeOrganizer.infrastructure.persistence;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.application.endpoint.activity.role.command;

public class CreateActivityRoleEndpoint(AppDbContext dbContext)
    : BaseCreateEndpoint<ActivityRole, ActivityRoleRequest>(dbContext)
{
    public override void Configure()
    {
        base.Configure();
        Validator<ActivityRoleValidator>();
    }
}