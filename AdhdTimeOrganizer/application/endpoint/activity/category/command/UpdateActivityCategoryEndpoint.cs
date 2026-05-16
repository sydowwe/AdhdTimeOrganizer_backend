using AdhdTimeOrganizer.application.dto.request.@base;
using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.application.mapper.activity;
using AdhdTimeOrganizer.domain.model.entity.activity;
using AdhdTimeOrganizer.application.validator;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activity.category.command;

public class UpdateActivityCategoryEndpoint(AppDbContext dbContext, ActivityCategoryMapper mapper)
    : BaseUpdateEndpoint<ActivityCategory, NameTextColorIconRequest, ActivityCategoryMapper>(dbContext, mapper)
{
    public override void Configure()
    {
        base.Configure();
        Validator<ActivityCategoryValidator>();
    }
}
