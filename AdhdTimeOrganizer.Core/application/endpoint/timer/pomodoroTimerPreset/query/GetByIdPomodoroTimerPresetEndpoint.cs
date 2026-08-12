using AdhdTimeOrganizer.Core.application.dto.response.timer;
using AdhdTimeOrganizer.Core.domain.model.entity.timer;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.endpoint.@base.read;

namespace AdhdTimeOrganizer.Core.application.endpoint.timer.pomodoroTimerPreset.query;

public class GetByIdPomodoroTimerPresetEndpoint(
    DbContext dbContext)
    : BaseGetByIdEndpoint<PomodoroTimerPreset, PomodoroTimerPresetResponse>(dbContext)
{
    // Scoped by AppDbContext's global IEntityWithUser query filter, so a foreign row never projects.
    protected override Task<bool> AuthorizeAsync(PomodoroTimerPresetResponse entity, CancellationToken ct) => Task.FromResult(true);
}