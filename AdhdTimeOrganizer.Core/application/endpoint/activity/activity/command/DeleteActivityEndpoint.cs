using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.Core.domain.serviceContract;
using AdhdTimeOrganizer.Core.infrastructure.persistence;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.Core.application.endpoint.activity.activity.command;

public class DeleteActivityEndpoint(DbContext dbContext, IActivityRemovalService removal)
    : BaseDeleteEndpoint<Activity>(dbContext)
{
    private readonly DbContext _dbContext = dbContext;
    private Func<CancellationToken, Task>? _afterCommit;

    /// <summary>
    /// The archive view lists archived activities and offers the same delete row action on them, so the
    /// handler has to be able to load one. Without this the button 404s on exactly the rows a user is
    /// most likely to delete.
    /// </summary>
    protected override string[] IgnoredQueryFilters => [AppQueryFilters.ActivityArchived];

    /// <summary>
    /// The cascade takes every row hanging off the activity without passing through its slice, so each slice that
    /// holds something outside the database for those rows reads what it needs now, while they still exist.
    /// </summary>
    protected override async Task BeforeDeleteAsync(Activity entity, CancellationToken ct = default) =>
        _afterCommit = await removal.BeforeRemovalAsync(_dbContext, [entity.Id], ct);

    protected override Task AfterSave(Activity entity, CancellationToken ct = default) =>
        _afterCommit?.Invoke(ct) ?? Task.CompletedTask;
}
