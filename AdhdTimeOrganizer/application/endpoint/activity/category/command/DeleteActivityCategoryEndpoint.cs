using AdhdTimeOrganizer.domain.model.entity.activity;
using AdhdTimeOrganizer.infrastructure.persistence;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.application.endpoint.activity.category.command;

public class DeleteActivityCategoryEndpoint(AppDbContext dbContext)
    : BaseDeleteEndpoint<ActivityCategory>(dbContext);