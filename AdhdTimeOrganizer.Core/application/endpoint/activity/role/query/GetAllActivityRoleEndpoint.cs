using AdhdTimeOrganizer.Core.application.dto.response.activity;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using Sydowwe.Framework.application.endpoint.@base.read;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.Core.application.endpoint.activity.role.query;

public class GetAllActivityRoleEndpoint(
    DbContext dbContext)
    : BaseGetAllEndpoint<ActivityRole, ActivityRoleResponse>(dbContext)
{
}