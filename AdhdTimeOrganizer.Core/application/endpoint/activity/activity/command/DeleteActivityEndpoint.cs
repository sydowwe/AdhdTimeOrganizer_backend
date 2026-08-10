using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using Sydowwe.Framework.application.endpoint.@base.command;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.Core.application.endpoint.activity.activity.command;

public class DeleteActivityEndpoint(DbContext dbContext)
    : BaseDeleteEndpoint<Activity>(dbContext);