using AdhdTimeOrganizer.application.dto.response.timer;
using AdhdTimeOrganizer.domain.model.entity.timer;
using AdhdTimeOrganizer.infrastructure.persistence;
using Sydowwe.Framework.application.endpoint.@base.read;

namespace AdhdTimeOrganizer.application.endpoint.timer.timerPreset.query;

public class GetByIdTimerPresetEndpoint(
    AppDbContext dbContext)
    : BaseGetByIdEndpoint<TimerPreset, TimerPresetResponse>(dbContext)
{
    // Scoped by AppDbContext's global IEntityWithUser query filter, so a foreign row never projects.
    protected override Task<bool> AuthorizeAsync(TimerPresetResponse entity, CancellationToken ct) => Task.FromResult(true);
}