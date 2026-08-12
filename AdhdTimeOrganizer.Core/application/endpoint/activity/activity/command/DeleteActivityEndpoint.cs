using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.Core.application.endpoint.activity.activity.command;

public class DeleteActivityEndpoint(DbContext dbContext)
    : BaseDeleteEndpoint<Activity>(dbContext);