using AdhdTimeOrganizer.domain.model.entity.activity;
using AdhdTimeOrganizer.infrastructure.persistence;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.application.endpoint.activity.activity.command;

public class DeleteActivityEndpoint(AppDbContext dbContext)
    : BaseDeleteEndpoint<Activity>(dbContext);