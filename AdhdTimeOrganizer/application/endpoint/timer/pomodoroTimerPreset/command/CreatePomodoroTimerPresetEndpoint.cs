using AdhdTimeOrganizer.application.dto.request.timer;
using AdhdTimeOrganizer.application.validator;
using AdhdTimeOrganizer.domain.model.entity.timer;
using AdhdTimeOrganizer.infrastructure.persistence;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.application.endpoint.timer.pomodoroTimerPreset.command;

public class CreatePomodoroTimerPresetEndpoint(AppDbContext dbContext)
    : BaseCreateEndpoint<PomodoroTimerPreset, PomodoroTimerPresetRequest>(dbContext)
{
    public override void Configure()
    {
        base.Configure();
        Validator<PomodoroTimerPresetValidator>();
    }
}