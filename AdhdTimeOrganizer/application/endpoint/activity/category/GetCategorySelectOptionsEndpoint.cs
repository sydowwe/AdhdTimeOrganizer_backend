using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.application.mapper.activity;
using AdhdTimeOrganizer.domain.model.entity.activity;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activity;

public class GetCategorySelectOptionsEndpoint(
    AppCommandDbContext appDbContext,
    CategoryMapper mapper)
    : BaseGetSelectOptionsEndpoint<ActivityCategory, CategoryMapper>(appDbContext, mapper)
{
}
