using AdhdTimeOrganizer.application.dto.request.timer;
using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.application.mapper.timer;
using AdhdTimeOrganizer.domain.model.entity.timer;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.timer.pomodoroTimerPreset.command;

public class CreatePomodoroTimerPresetEndpoint(AppCommandDbContext dbContext, PomodoroTimerPresetMapper mapper)
    : BaseCreateEndpoint<PomodoroTimerPreset, PomodoroTimerPresetRequest, PomodoroTimerPresetMapper>(dbContext, mapper);
