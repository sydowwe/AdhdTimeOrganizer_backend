
using AdhdTimeOrganizer.application.dto.response;
using AdhdTimeOrganizer.application.dto.response.activity;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.application.mapper.activity;
using AdhdTimeOrganizer.domain.model.entity.activity;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activity;

public class GetAllCategoriesEndpoint(
    AppCommandDbContext dbContext,
    CategoryMapper mapper)
    : BaseGetAllEndpoint<ActivityCategory, CategoryResponse, CategoryMapper>(dbContext, mapper)
{
}
