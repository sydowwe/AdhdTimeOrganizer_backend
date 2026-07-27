using AdhdTimeOrganizer.application.dto.response.timer;
using AdhdTimeOrganizer.domain.model.entity.timer;
using AdhdTimeOrganizer.infrastructure.persistence;
using Sydowwe.Framework.application.endpoint.@base.read;

namespace AdhdTimeOrganizer.application.endpoint.timer.timerPreset.query;

public class GetAllTimerPresetEndpoint(
    AppDbContext dbContext)
    : BaseGetAllEndpoint<TimerPreset, TimerPresetResponse>(dbContext)
{
}