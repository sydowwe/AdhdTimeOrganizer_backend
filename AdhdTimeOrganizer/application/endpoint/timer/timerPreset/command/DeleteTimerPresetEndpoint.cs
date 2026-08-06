using AdhdTimeOrganizer.domain.model.entity.timer;
using AdhdTimeOrganizer.infrastructure.persistence;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.application.endpoint.timer.timerPreset.command;

public class DeleteTimerPresetEndpoint(AppDbContext dbContext)
    : BaseDeleteEndpoint<TimerPreset>(dbContext);