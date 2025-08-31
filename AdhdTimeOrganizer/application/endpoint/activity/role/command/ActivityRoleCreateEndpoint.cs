using AdhdTimeOrganizer.application.dto.request.@base;
using AdhdTimeOrganizer.application.dto.response;
using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.application.mapper.activity;
using AdhdTimeOrganizer.domain.model.entity.activity;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activity.role.command;

public class ActivityRoleCreateEndpoint(AppCommandDbContext dbContext, ActivityRoleMapper mapper)
    : BaseCreateEndpoint<ActivityRole, NameTextColorIconRequest, ActivityRoleMapper>(dbContext, mapper);
