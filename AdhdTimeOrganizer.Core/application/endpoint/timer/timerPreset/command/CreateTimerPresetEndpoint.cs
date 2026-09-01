using AdhdTimeOrganizer.Core.application.dto.request.timer;
using AdhdTimeOrganizer.Core.application.validator;
using AdhdTimeOrganizer.Core.domain.model.entity.timer;
using AdhdTimeOrganizer.Core.infrastructure.persistence.extensions;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.Core.application.endpoint.timer.timerPreset.command;

public class CreateTimerPresetEndpoint(DbContext dbContext)
    : BaseCreateEndpoint<TimerPreset, TimerPresetRequest>(dbContext)
{
    public override void Configure()
    {
        base.Configure();
        Validator<TimerPresetValidator>();
    }

    /// <summary>
    /// The FK cascades, so a preset pointing at another user's activity is destroyed when that user
    /// deletes it — and their <c>usageCount</c> read the activity as 0, because this row is invisible
    /// behind their user filter. Ownership has to be checked here.
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
