using AdhdTimeOrganizer.application.dto.response.timer;
using AdhdTimeOrganizer.domain.model.entity.timer;
using AdhdTimeOrganizer.infrastructure.persistence;
using Sydowwe.Framework.application.endpoint.@base.read;

namespace AdhdTimeOrganizer.application.endpoint.timer.pomodoroTimerPreset.query;

public class GetByIdPomodoroTimerPresetEndpoint(
    AppDbContext dbContext)
    : BaseGetByIdEndpoint<PomodoroTimerPreset, PomodoroTimerPresetResponse>(dbContext)
{
    // Scoped by AppDbContext's global IEntityWithUser query filter, so a foreign row never projects.
    protected override Task<bool> AuthorizeAsync(PomodoroTimerPresetResponse entity, CancellationToken ct) => Task.FromResult(true);
}