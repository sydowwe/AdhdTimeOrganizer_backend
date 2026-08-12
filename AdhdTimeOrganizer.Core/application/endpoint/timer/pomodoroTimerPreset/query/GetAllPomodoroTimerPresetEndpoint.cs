using AdhdTimeOrganizer.Core.application.dto.response.timer;
using AdhdTimeOrganizer.Core.domain.model.entity.timer;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.endpoint.@base.read;

namespace AdhdTimeOrganizer.Core.application.endpoint.timer.pomodoroTimerPreset.query;

public class GetAllPomodoroTimerPresetEndpoint(
    DbContext dbContext)
    : BaseGetAllEndpoint<PomodoroTimerPreset, PomodoroTimerPresetResponse>(dbContext)
{
}