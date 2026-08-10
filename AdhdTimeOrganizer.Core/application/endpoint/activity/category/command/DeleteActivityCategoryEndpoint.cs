using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using Sydowwe.Framework.application.endpoint.@base.command;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.Core.application.endpoint.activity.category.command;

public class DeleteActivityCategoryEndpoint(DbContext dbContext)
    : BaseDeleteEndpoint<ActivityCategory>(dbContext);