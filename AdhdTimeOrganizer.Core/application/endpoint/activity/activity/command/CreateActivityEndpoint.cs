using AdhdTimeOrganizer.Core.application.dto.request.activity;
using AdhdTimeOrganizer.Core.application.validator;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.Core.infrastructure.persistence.extensions;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.Core.application.endpoint.activity.activity.command;

public class CreateActivityEndpoint(DbContext dbContext)
    : BaseCreateEndpoint<Activity, ActivityRequest>(dbContext)
{
    public override void Configure()
    {
        base.Configure();
        Validator<ActivityValidator>();
    }

    /// <summary>
    /// <c>ActivityValidator</c> only checks the ids are non-zero. Another user's role or category id
    /// satisfies the FK constraint, so this is the only thing standing between the request and a row
    /// that drops out of the caller's own grid (the required <c>Role</c> join is user-filtered).
    /// </summary>
    protected override async Task<bool> AfterMapping(Activity entity, ActivityRequest req, CancellationToken ct = default)
    {
        if (await DbContext.AllVisibleAsync<ActivityRole>(ct, req.RoleId)
            && await DbContext.AllVisibleAsync<ActivityCategory>(ct, req.CategoryId))
            return true;

        AddError("Activity role or category not found.");
        await Send.ErrorsAsync(404, ct);
        return false;
    }
}
