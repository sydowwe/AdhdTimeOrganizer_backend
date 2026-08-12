using AdhdTimeOrganizer.Core.domain.model.entity.timer;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.Core.application.endpoint.timer.pomodoroTimerPreset.command;

public class DeletePomodoroTimerPresetEndpoint(DbContext dbContext)
    : BaseDeleteEndpoint<PomodoroTimerPreset>(dbContext);