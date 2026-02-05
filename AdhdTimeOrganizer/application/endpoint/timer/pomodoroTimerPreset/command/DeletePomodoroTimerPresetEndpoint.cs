using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.domain.model.entity.timer;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.timer.pomodoroTimerPreset.command;

public class DeletePomodoroTimerPresetEndpoint(AppDbContext dbContext)
    : BaseDeleteEndpoint<PomodoroTimerPreset>(dbContext);
