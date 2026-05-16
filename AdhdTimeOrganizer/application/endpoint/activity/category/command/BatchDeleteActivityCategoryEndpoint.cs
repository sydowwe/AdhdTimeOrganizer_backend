using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.domain.model.entity.activity;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activity.category.command;

public class BatchDeleteActivityCategoryEndpoint(AppDbContext dbContext) : BaseBatchDeleteEndpoint<ActivityCategory>(dbContext);