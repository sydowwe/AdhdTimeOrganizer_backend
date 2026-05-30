using AdhdTimeOrganizer.application.dto.request.@base;
using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.domain.model.entity.activity;
using AdhdTimeOrganizer.application.validator;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activity.role.command;

public class CreateActivityRoleEndpoint(AppDbContext dbContext)
    : BaseCreateEndpoint<ActivityRole, NameTextColorIconRequest>(dbContext)
{
    public override void Configure()
    {
        base.Configure();
        Validator<ActivityRoleValidator>();
    }
}
