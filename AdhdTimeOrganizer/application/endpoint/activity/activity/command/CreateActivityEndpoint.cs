using AdhdTimeOrganizer.application.dto.request.activity;
using AdhdTimeOrganizer.application.validator;
using AdhdTimeOrganizer.domain.model.entity.activity;
using AdhdTimeOrganizer.infrastructure.persistence;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.application.endpoint.activity.activity.command;

public class CreateActivityEndpoint(AppDbContext dbContext)
    : BaseCreateEndpoint<Activity, ActivityRequest>(dbContext)
{
    public override void Configure()
    {
        base.Configure();
        Validator<ActivityValidator>();
    }
}