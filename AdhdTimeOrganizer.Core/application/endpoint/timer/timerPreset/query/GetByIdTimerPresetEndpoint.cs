using AdhdTimeOrganizer.Core.application.dto.response.timer;
using AdhdTimeOrganizer.Core.domain.model.entity.timer;
using Sydowwe.Framework.application.endpoint.@base.read;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.Core.application.endpoint.timer.timerPreset.query;

public class GetByIdTimerPresetEndpoint(
    DbContext dbContext)
    : BaseGetByIdEndpoint<TimerPreset, TimerPresetResponse>(dbContext)
{
    // Scoped by AppDbContext's global IEntityWithUser query filter, so a foreign row never projects.
    protected override Task<bool> AuthorizeAsync(TimerPresetResponse entity, CancellationToken ct) => Task.FromResult(true);
}