using AdhdTimeOrganizer.application.dto.request.timer;
using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.application.mapper.timer;
using AdhdTimeOrganizer.application.validator;
using AdhdTimeOrganizer.domain.model.entity.timer;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.timer.pomodoroTimerPreset.command;

public class CreatePomodoroTimerPresetEndpoint(AppDbContext dbContext, PomodoroTimerPresetMapper mapper)
    : BaseCreateEndpoint<PomodoroTimerPreset, PomodoroTimerPresetRequest, PomodoroTimerPresetMapper>(dbContext, mapper)
{
    public override void Configure()
    {
        base.Configure();
        Validator<PomodoroTimerPresetValidator>();
    }
}
