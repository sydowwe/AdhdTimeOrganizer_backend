using AdhdTimeOrganizer.Core.application.dto.request.activity;
using AdhdTimeOrganizer.Core.application.validator;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.infrastructure.persistence;
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
}