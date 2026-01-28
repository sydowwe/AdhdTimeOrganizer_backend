using AdhdTimeOrganizer.application.dto.request.timer;
using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.application.mapper.timer;
using AdhdTimeOrganizer.domain.model.entity.timer;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.timer.timerPreset.command;

public class CreateTimerPresetEndpoint(AppCommandDbContext dbContext, TimerPresetMapper mapper)
    : BaseCreateEndpoint<TimerPreset, TimerPresetRequest, TimerPresetMapper>(dbContext, mapper);
