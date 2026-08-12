using AdhdTimeOrganizer.Core.application.dto.request.timer;
using AdhdTimeOrganizer.Core.application.validator;
using AdhdTimeOrganizer.Core.domain.model.entity.timer;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.Core.application.endpoint.timer.timerPreset.command;

public class UpdateTimerPresetEndpoint(DbContext dbContext)
    : BaseUpdateEndpoint<TimerPreset, TimerPresetRequest>(dbContext)
{
    public override void Configure()
    {
        base.Configure();
        Validator<TimerPresetValidator>();
    }
}