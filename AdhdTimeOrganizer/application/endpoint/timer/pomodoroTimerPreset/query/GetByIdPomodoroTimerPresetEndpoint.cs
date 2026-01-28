using AdhdTimeOrganizer.application.dto.response.timer;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.application.mapper.timer;
using AdhdTimeOrganizer.domain.model.entity.timer;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.timer.pomodoroTimerPreset.query;

public class GetByIdPomodoroTimerPresetEndpoint(
    AppCommandDbContext dbContext,
    PomodoroTimerPresetMapper mapper)
    : BaseGetByIdEndpoint<PomodoroTimerPreset, PomodoroTimerPresetResponse, PomodoroTimerPresetMapper>(dbContext, mapper)
{
}
