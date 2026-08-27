using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.infrastructure.persistence;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.Core.application.endpoint.activity.activity.command;

public class DeleteActivityEndpoint(DbContext dbContext)
    : BaseDeleteEndpoint<Activity>(dbContext)
{
    /// <summary>
    /// The archive view lists archived activities and offers the same delete row action on them, so the
    /// handler has to be able to load one. Without this the button 404s on exactly the rows a user is
    /// most likely to delete.
    /// </summary>
    protected override string[] IgnoredQueryFilters => [AppQueryFilters.ActivityArchived];
}
