using AdhdTimeOrganizer.application.dto.response.activity;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.domain.model.entity.activity;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activity.role.query;

public class GetAllActivityRoleEndpoint(
    AppDbContext dbContext)
    : BaseGetAllEndpoint<ActivityRole, ActivityRoleResponse>(dbContext)
{
}
