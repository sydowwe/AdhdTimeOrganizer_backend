using AdhdTimeOrganizer.application.dto.request.activity;
using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.application.mapper.activity;
using AdhdTimeOrganizer.application.validator;
using AdhdTimeOrganizer.domain.model.entity.activity;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activity.activity.command;

public class UpdateActivityEndpoint(AppDbContext dbContext, ActivityMapper mapper)
    : BaseUpdateEndpoint<Activity, ActivityRequest, ActivityMapper>(dbContext, mapper)
{
    public override void Configure()
    {
        base.Configure();
        Validator<ActivityValidator>();
    }
}
