using AdhdTimeOrganizer.application.dto.response;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.application.mapper.activity;
using AdhdTimeOrganizer.domain.model.entity.activity;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activity.category.query;

public class GetByIdActivityCategoryEndpoint(
    AppCommandDbContext dbContext,
    ActivityCategoryMapper mapper)
    : BaseGetByIdEndpoint<ActivityCategory, ActivityCategoryResponse, ActivityCategoryMapper>(dbContext, mapper)
{
}
