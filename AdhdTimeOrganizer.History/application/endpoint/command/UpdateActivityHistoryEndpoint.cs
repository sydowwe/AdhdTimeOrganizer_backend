using AdhdTimeOrganizer.Core.domain.serviceContract;
using AdhdTimeOrganizer.History.application.dto.request.history;
using AdhdTimeOrganizer.History.application.validator;
using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.endpoint.@base.command;
using Sydowwe.Framework.application.extensions;

namespace AdhdTimeOrganizer.History.application.endpoint.activityHistory.activityHistory.command;

public class UpdateActivityHistoryEndpoint(DbContext dbContext, IReferencedIdOwnershipService ownership)
    : BaseUpdateEndpoint<ActivityHistory, ActivityHistoryRequest>(dbContext)
{
    public override void Configure()
    {
        base.Configure();
        Validator<ActivityHistoryValidator>();
    }

    /// <summary>
    /// Same referenced-id ownership check as the create path — an edit can repoint <c>ActivityId</c> at
    /// another user's activity just as easily as an insert can. Runs before the row is loaded, so a
    /// foreign referenced id is refused whether or not the row being edited exists.
    /// </summary>
    protected override async Task<bool> BeforeMapping(ActivityHistoryRequest req, CancellationToken ct = default)
    {
        var unowned = await ActivityHistoryReferencedIds.FirstUnownedAsync(req, ownership, DbContext, User.GetId(), ct);
        if (unowned == null)
            return true;

        ValidationFailures.Add(unowned);
        await Send.ErrorsAsync(404, ct);
        return false;
    }
}
