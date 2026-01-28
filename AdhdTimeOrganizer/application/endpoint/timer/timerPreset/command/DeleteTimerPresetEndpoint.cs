using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.domain.model.entity.timer;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.timer.timerPreset.command;

public class DeleteTimerPresetEndpoint(AppCommandDbContext dbContext)
    : BaseDeleteEndpoint<TimerPreset>(dbContext);
