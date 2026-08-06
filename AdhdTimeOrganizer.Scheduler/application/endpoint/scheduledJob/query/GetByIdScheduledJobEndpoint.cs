using AdhdTimeOrganizer.Scheduler.application.dto.scheduledJob;
using AdhdTimeOrganizer.Scheduler.domain.entity;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.endpoint.@base.read;

namespace AdhdTimeOrganizer.Scheduler.application.endpoint.scheduledJob.query;

/// <summary>Single registry row by id. Open to any signed-in user (infra).</summary>
public class GetByIdScheduledJobEndpoint(DbContext dbContext)
    : BaseGetByIdEndpoint<ScheduledJob, ScheduledJobDto>(dbContext)
{
    protected override Task<bool> AuthorizeAsync(ScheduledJobDto entity, CancellationToken ct) => Task.FromResult(true);
}