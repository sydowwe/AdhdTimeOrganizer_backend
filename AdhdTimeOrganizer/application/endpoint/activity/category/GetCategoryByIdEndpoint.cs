using AdhdTimeOrganizer.application.dto.response;
using AdhdTimeOrganizer.application.dto.response.activity;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.application.mapper.activity;
using AdhdTimeOrganizer.domain.model.entity.activity;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activity;

public class GetCategoryByIdEndpoint(
    AppCommandDbContext dbContext,
    CategoryMapper mapper)
    : BaseGetByIdEndpoint<ActivityCategory, CategoryResponse, CategoryMapper>(dbContext, mapper)
{
}
