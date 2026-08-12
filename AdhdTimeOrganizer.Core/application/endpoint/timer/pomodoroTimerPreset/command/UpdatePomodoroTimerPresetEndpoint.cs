using AdhdTimeOrganizer.Core.application.dto.request.timer;
using AdhdTimeOrganizer.Core.application.validator;
using AdhdTimeOrganizer.Core.domain.model.entity.timer;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.Core.application.endpoint.timer.pomodoroTimerPreset.command;

public class UpdatePomodoroTimerPresetEndpoint(DbContext dbContext)
    : BaseUpdateEndpoint<PomodoroTimerPreset, PomodoroTimerPresetRequest>(dbContext)
{
    public override void Configure()
    {
        base.Configure();
        Validator<PomodoroTimerPresetValidator>();
    }
}