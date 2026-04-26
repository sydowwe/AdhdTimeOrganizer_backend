using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.application.mapper.activity;
using AdhdTimeOrganizer.domain.model.entity.activity;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activity.category.query;

public class GetSelectOptionsActivityRoleEndpoint(
    AppDbContext appDbContext,
    ActivityRoleMapper mapper)
    : BaseGetSelectOptionsEndpoint<ActivityRole, ActivityRoleMapper>(appDbContext, mapper)
{
}
