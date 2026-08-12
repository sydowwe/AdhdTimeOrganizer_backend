using AdhdTimeOrganizer.Core.application.dto.response.activity;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.endpoint.@base.read;

namespace AdhdTimeOrganizer.Core.application.endpoint.activity.role.query;

public class GetAllActivityRoleEndpoint(
    DbContext dbContext)
    : BaseGetAllEndpoint<ActivityRole, ActivityRoleResponse>(dbContext)
{
}