using AdhdTimeOrganizer.Core.domain.model.entity.timer;
using Sydowwe.Framework.application.endpoint.@base.command;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.Core.application.endpoint.timer.pomodoroTimerPreset.command;

public class DeletePomodoroTimerPresetEndpoint(DbContext dbContext)
    : BaseDeleteEndpoint<PomodoroTimerPreset>(dbContext);