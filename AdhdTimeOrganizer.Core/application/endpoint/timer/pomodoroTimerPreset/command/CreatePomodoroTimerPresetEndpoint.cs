using AdhdTimeOrganizer.Core.application.dto.request.timer;
using AdhdTimeOrganizer.Core.application.validator;
using AdhdTimeOrganizer.Core.domain.model.entity.timer;
using AdhdTimeOrganizer.Core.infrastructure.persistence.extensions;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.Core.application.endpoint.timer.pomodoroTimerPreset.command;

public class CreatePomodoroTimerPresetEndpoint(DbContext dbContext)
    : BaseCreateEndpoint<PomodoroTimerPreset, PomodoroTimerPresetRequest>(dbContext)
{
    public override void Configure()
    {
        base.Configure();
        Validator<PomodoroTimerPresetValidator>();
    }

    /// <summary>
    /// Both activity FKs cascade, so a preset pointing at another user's activity is destroyed when
    /// that user deletes it — while their <c>usageCount</c> reported 0. Ownership is checked here.
    /// </summary>
    protected override async Task<bool> AfterMapping(PomodoroTimerPreset entity, PomodoroTimerPresetRequest req,
        CancellationToken ct = default)
    {
        if (await DbContext.AllActivitiesVisibleAsync(ct, req.FocusActivityId, req.RestActivityId))
            return true;

        AddError("Activity not found.");
        await Send.ErrorsAsync(404, ct);
        return false;
    }
}
