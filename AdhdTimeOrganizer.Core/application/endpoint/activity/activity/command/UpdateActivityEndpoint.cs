using AdhdTimeOrganizer.Core.application.dto.request.activity;
using AdhdTimeOrganizer.Core.application.validator;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.Core.infrastructure.persistence.extensions;
using AdhdTimeOrganizer.Core.infrastructure.persistence;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.Core.application.endpoint.activity.activity.command;

public class UpdateActivityEndpoint(DbContext dbContext)
    : BaseUpdateEndpoint<Activity, ActivityRequest>(dbContext)
{
    /// <summary>
    /// An archived activity is still editable — the archive view opens the same form. Lifting the
    /// archival filter is safe here precisely because <c>ActivityRequest</c> does not carry
    /// <c>IsArchived</c>, so the edit cannot move the flag; only
    /// <c>SetArchivedActivityEndpoint</c> does that.
    /// </summary>
    protected override string[] IgnoredQueryFilters => [AppQueryFilters.ActivityArchived];

    public override void Configure()
    {
        base.Configure();
        Validator<ActivityValidator>();
    }

    /// <summary>
    /// Same guard as <c>CreateActivityEndpoint</c> — an update repoints the same two FKs, so it hands
    /// out the same existence oracle and can strand the row behind the user-filtered <c>Role</c> join.
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
