using AdhdTimeOrganizer.Core.application.dto.response.timer;
using AdhdTimeOrganizer.Core.domain.model.entity.timer;
using Sydowwe.Framework.application.endpoint.@base.read;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.Core.application.endpoint.timer.timerPreset.query;

public class GetAllTimerPresetEndpoint(
    DbContext dbContext)
    : BaseGetAllEndpoint<TimerPreset, TimerPresetResponse>(dbContext)
{
}