using AdhdTimeOrganizer.Core.domain.serviceContract;
using AdhdTimeOrganizer.History.application.dto.request.history;
using AdhdTimeOrganizer.History.application.validator;
using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.History.domain.serviceContract;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.endpoint.@base.command;
using Sydowwe.Framework.application.extensions;

namespace AdhdTimeOrganizer.History.application.endpoint.activityHistory.activityHistory.command;

public class CreateActivityHistoryEndpoint(
    DbContext dbContext,
    IReferencedIdOwnershipService ownership,
    IActivityTimeRecordedPublisher timeRecorded)
    : BaseCreateEndpoint<ActivityHistory, ActivityHistoryRequest>(dbContext)
{
    /// <summary>Per-request state; FastEndpoints builds an endpoint instance per request.</summary>
    private bool _countsAsRepetition;

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
        // Kept for AfterSave, which sees only the entity. The flag is not a column: it says what this request
        // meant, and the row outlives the request.
        _countsAsRepetition = req.CountsAsRepetition;

        var unowned = await ActivityHistoryReferencedIds.FirstUnownedAsync(req, ownership, DbContext, User.GetId(), ct);
        if (unowned == null)
            return true;

        ValidationFailures.Add(unowned);
        await Send.ErrorsAsync(404, ct);
        return false;
    }

    /// <summary>
    /// Logged time ticks off the plan like tracked time does (FO2). Before the 201, so a client that refetches
    /// on success already sees the block or item it completed. The publisher swallows its own failures, so
    /// this cannot reach the base's catch and turn a committed row into an error.
    /// </summary>
    protected override Task AfterSave(ActivityHistory entity, CancellationToken ct = default) =>
        timeRecorded.PublishAsync(DbContext, User.GetId(), [entity], _countsAsRepetition, ct);
}
