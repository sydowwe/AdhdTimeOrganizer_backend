using AdhdTimeOrganizer.application.dto.response.activity;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.domain.model.entity.activity;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activity.category.query;

public class GetAllActivityCategoryEndpoint(
    AppDbContext dbContext)
    : BaseGetAllEndpoint<ActivityCategory, ActivityCategoryResponse>(dbContext)
{
}
