using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.Core.application.endpoint.activity.category.command;

public class DeleteActivityCategoryEndpoint(DbContext dbContext)
    : BaseDeleteEndpoint<ActivityCategory>(dbContext);