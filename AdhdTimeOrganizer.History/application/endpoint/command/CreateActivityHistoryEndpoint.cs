using AdhdTimeOrganizer.Core.domain.serviceContract;
using AdhdTimeOrganizer.History.application.dto.request.history;
using AdhdTimeOrganizer.History.application.validator;
using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.endpoint.@base.command;
using Sydowwe.Framework.application.extensions;

namespace AdhdTimeOrganizer.History.application.endpoint.activityHistory.activityHistory.command;

public class CreateActivityHistoryEndpoint(DbContext dbContext, IReferencedIdOwnershipService ownership)
    : BaseCreateEndpoint<ActivityHistory, ActivityHistoryRequest>(dbContext)
{
    public override void Configure()
    {
        base.Configure();
        Validator<ActivityHistoryValidator>();
    }

    /// <summary>
    /// Refuses a recording that references an activity or an item the caller does not own — see
    /// <see cref="ActivityHistoryReferencedIds"/> for why nothing else can. 404 on the offending field,
    /// which is what a nonexistent id already answers.
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
