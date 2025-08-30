using AdhdTimeOrganizer.application.dto.response;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.application.mapper.activity;
using AdhdTimeOrganizer.domain.model.entity.activity;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activity.category.query;

public class GetAllCategoriesEndpoint(
    AppCommandDbContext dbContext,
    ActivityCategoryMapper mapper)
    : BaseGetAllEndpoint<ActivityCategory, ActivityCategoryResponse, ActivityCategoryMapper>(dbContext, mapper)
{
}
