using AdhdTimeOrganizer.Core.application.dto.request.timer;
using AdhdTimeOrganizer.Core.application.validator;
using AdhdTimeOrganizer.Core.domain.model.entity.timer;
using AdhdTimeOrganizer.Core.infrastructure.persistence.extensions;
using FastEndpoints;
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

    /// <summary>
    /// Same guard as <c>CreateTimerPresetEndpoint</c> — an update repoints the same cascading FK.
    /// </summary>
    protected override async Task<bool> AfterMapping(TimerPreset entity, TimerPresetRequest req, CancellationToken ct = default)
    {
        if (await DbContext.AllActivitiesVisibleAsync(ct, req.ActivityId))
            return true;

        AddError("Activity not found.");
        await Send.ErrorsAsync(404, ct);
        return false;
    }
}
