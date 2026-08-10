using AdhdTimeOrganizer.Core.domain.model.entity.timer;
using Sydowwe.Framework.application.endpoint.@base.command;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.Core.application.endpoint.timer.timerPreset.command;

public class DeleteTimerPresetEndpoint(DbContext dbContext)
    : BaseDeleteEndpoint<TimerPreset>(dbContext);