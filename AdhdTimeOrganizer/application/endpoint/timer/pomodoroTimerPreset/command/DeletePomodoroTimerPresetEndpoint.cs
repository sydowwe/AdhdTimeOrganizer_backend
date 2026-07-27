using AdhdTimeOrganizer.domain.model.entity.timer;
using AdhdTimeOrganizer.infrastructure.persistence;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.application.endpoint.timer.pomodoroTimerPreset.command;

public class DeletePomodoroTimerPresetEndpoint(AppDbContext dbContext)
    : BaseDeleteEndpoint<PomodoroTimerPreset>(dbContext);