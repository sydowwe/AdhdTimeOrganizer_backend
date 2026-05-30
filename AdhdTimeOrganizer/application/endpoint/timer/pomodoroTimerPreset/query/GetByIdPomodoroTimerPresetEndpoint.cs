using AdhdTimeOrganizer.application.dto.response.timer;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.domain.model.entity.timer;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.timer.pomodoroTimerPreset.query;

public class GetByIdPomodoroTimerPresetEndpoint(
    AppDbContext dbContext)
    : BaseGetByIdEndpoint<PomodoroTimerPreset, PomodoroTimerPresetResponse>(dbContext)
{
}
