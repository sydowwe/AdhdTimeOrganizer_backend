using AdhdTimeOrganizer.application.dto.request.@base;
using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.application.mapper.activity;
using AdhdTimeOrganizer.domain.model.entity.activity;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activity.category.command;

public class CreateActivityCategoryEndpoint(AppCommandDbContext dbContext, ActivityCategoryMapper mapper)
    : BaseCreateEndpoint<ActivityCategory, NameTextColorIconRequest, ActivityCategoryMapper>(dbContext, mapper);