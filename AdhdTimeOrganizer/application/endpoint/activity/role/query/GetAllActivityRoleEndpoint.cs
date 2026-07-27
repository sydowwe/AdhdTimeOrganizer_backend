using AdhdTimeOrganizer.application.dto.response.activity;
using AdhdTimeOrganizer.domain.model.entity.activity;
using AdhdTimeOrganizer.infrastructure.persistence;
using Sydowwe.Framework.application.endpoint.@base.read;

namespace AdhdTimeOrganizer.application.endpoint.activity.role.query;

public class GetAllActivityRoleEndpoint(
    AppDbContext dbContext)
    : BaseGetAllEndpoint<ActivityRole, ActivityRoleResponse>(dbContext)
{
}