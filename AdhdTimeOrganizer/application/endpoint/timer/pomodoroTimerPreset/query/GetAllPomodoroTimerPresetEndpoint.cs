using AdhdTimeOrganizer.application.dto.response.timer;
using AdhdTimeOrganizer.domain.model.entity.timer;
using AdhdTimeOrganizer.infrastructure.persistence;
using Sydowwe.Framework.application.endpoint.@base.read;

namespace AdhdTimeOrganizer.application.endpoint.timer.pomodoroTimerPreset.query;

public class GetAllPomodoroTimerPresetEndpoint(
    AppDbContext dbContext)
    : BaseGetAllEndpoint<PomodoroTimerPreset, PomodoroTimerPresetResponse>(dbContext)
{
}