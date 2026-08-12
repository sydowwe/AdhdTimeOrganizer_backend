using AdhdTimeOrganizer.Core.application.dto.response.timer;
using AdhdTimeOrganizer.Core.domain.model.entity.timer;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.endpoint.@base.read;

namespace AdhdTimeOrganizer.Core.application.endpoint.timer.timerPreset.query;

public class GetAllTimerPresetEndpoint(
    DbContext dbContext)
    : BaseGetAllEndpoint<TimerPreset, TimerPresetResponse>(dbContext)
{
}