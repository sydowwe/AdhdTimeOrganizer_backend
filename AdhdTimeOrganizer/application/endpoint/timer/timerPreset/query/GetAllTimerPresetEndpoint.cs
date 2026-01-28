using AdhdTimeOrganizer.application.dto.response.timer;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.application.mapper.timer;
using AdhdTimeOrganizer.domain.model.entity.timer;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.timer.timerPreset.query;

public class GetAllTimerPresetEndpoint(
    AppCommandDbContext dbContext,
    TimerPresetMapper mapper)
    : BaseGetAllEndpoint<TimerPreset, TimerPresetResponse, TimerPresetMapper>(dbContext, mapper)
{
}
