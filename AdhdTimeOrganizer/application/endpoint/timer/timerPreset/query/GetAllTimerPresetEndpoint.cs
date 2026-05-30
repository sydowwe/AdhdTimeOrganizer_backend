using AdhdTimeOrganizer.application.dto.response.timer;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.domain.model.entity.timer;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.timer.timerPreset.query;

public class GetAllTimerPresetEndpoint(
    AppDbContext dbContext)
    : BaseGetAllEndpoint<TimerPreset, TimerPresetResponse>(dbContext)
{
}
