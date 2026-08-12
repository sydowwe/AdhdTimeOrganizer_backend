using AdhdTimeOrganizer.Core.domain.model.entity.timer;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.Core.application.endpoint.timer.timerPreset.command;

public class DeleteTimerPresetEndpoint(DbContext dbContext)
    : BaseDeleteEndpoint<TimerPreset>(dbContext);