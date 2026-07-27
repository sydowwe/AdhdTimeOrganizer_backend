using AdhdTimeOrganizer.application.dto.request.activity;
using AdhdTimeOrganizer.application.validator;
using AdhdTimeOrganizer.domain.model.entity.activity;
using AdhdTimeOrganizer.infrastructure.persistence;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.application.endpoint.activity.category.command;

public class CreateActivityCategoryEndpoint(AppDbContext dbContext)
    : BaseCreateEndpoint<ActivityCategory, ActivityCategoryRequest>(dbContext)
{
    public override void Configure()
    {
        base.Configure();
        Validator<ActivityCategoryValidator>();
    }
}