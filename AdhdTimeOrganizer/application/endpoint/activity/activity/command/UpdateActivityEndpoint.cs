using AdhdTimeOrganizer.application.dto.request.activity;
using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.application.validator;
using AdhdTimeOrganizer.domain.model.entity.activity;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activity.activity.command;

public class UpdateActivityEndpoint(AppDbContext dbContext)
    : BaseUpdateEndpoint<Activity, ActivityRequest>(dbContext)
{
    public override void Configure()
    {
        base.Configure();
        Validator<ActivityValidator>();
    }
}
