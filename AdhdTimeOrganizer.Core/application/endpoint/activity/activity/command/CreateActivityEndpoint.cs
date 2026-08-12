using AdhdTimeOrganizer.Core.application.dto.request.activity;
using AdhdTimeOrganizer.Core.application.validator;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.Core.application.endpoint.activity.activity.command;

public class CreateActivityEndpoint(DbContext dbContext)
    : BaseCreateEndpoint<Activity, ActivityRequest>(dbContext)
{
    public override void Configure()
    {
        base.Configure();
        Validator<ActivityValidator>();
    }
}