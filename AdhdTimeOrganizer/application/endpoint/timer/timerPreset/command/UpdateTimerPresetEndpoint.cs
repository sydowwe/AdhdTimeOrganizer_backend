using AdhdTimeOrganizer.application.dto.request.timer;
using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.application.validator;
using AdhdTimeOrganizer.domain.model.entity.timer;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.timer.timerPreset.command;

public class UpdateTimerPresetEndpoint(AppDbContext dbContext)
    : BaseUpdateEndpoint<TimerPreset, TimerPresetRequest>(dbContext)
{
    public override void Configure()
    {
        base.Configure();
        Validator<TimerPresetValidator>();
    }
}
