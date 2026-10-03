using AdhdTimeOrganizer.Core.application.seam;
using AdhdTimeOrganizer.Core.domain.serviceContract;
using AdhdTimeOrganizer.History.application.dto.request.history;
using AdhdTimeOrganizer.History.application.dto.response.activityHistory;
using AdhdTimeOrganizer.History.application.validator;
using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;
using FastEndpoints;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.error;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.domain.helper;
using Sydowwe.Framework.infrastructure.persistence;

namespace AdhdTimeOrganizer.History.application.endpoint.activityHistory.activityHistory.command;

/// <summary>
/// Writes one finished Pomodoro or countdown — its outcome and one row per phase that ran — as a single unit.
/// </summary>
/// <remarks>
/// <para>
/// <b>All-or-nothing by construction:</b> the session and its rows are one graph added in one
/// <c>SaveChanges</c>, which is one transaction. A partial session cannot be observed, which is the whole reason
/// the client stopped firing one <c>POST /activity-history</c> per row.
/// </para>
/// <para>
/// Not a <c>BaseCreateEndpoint</c>: that base answers a bare id, and the client confirms the whole set landed
/// from the session id <i>and</i> every row id.
/// </para>
/// </remarks>
public class CreateActivityHistorySessionEndpoint(DbContext dbContext, IReferencedIdOwnershipService ownership)
    : Endpoint<CreateActivityHistorySessionRequest, CreateActivityHistorySessionResponse>
{
    public override void Configure()
    {
        Post("/activity-history/session");
        Roles(this.GetDefaultRoles());
        Validator<CreateActivityHistorySessionValidator>();
        Summary(s =>
        {
            s.Summary = "Record one finished Pomodoro or countdown as a session of history rows";
            s.Description = "Creates the session and every record in one transaction, or none of them. "
                            + "recordIds are returned in the order the records were sent.";
            s.Response<CreateActivityHistorySessionResponse>(201, "Created");
            s.Response(400, "Malformed session");
            s.Response(404, "An activity or linked item the caller does not own");
        });
    }

    public override async Task HandleAsync(CreateActivityHistorySessionRequest req, CancellationToken ct)
    {
        var userId = User.GetId();

        // The same check CreateActivityHistoryEndpoint makes, for every record — the row's UserId is stamped
        // here, so nothing else stops a record pointing at a stranger's activity (see
        // ActivityHistoryReferencedIds). Each distinct id is looked up once; the failure is keyed on the first
        // record carrying it, and "missing" and "foreign" stay indistinguishable.
        var checkedIds = new HashSet<long>();
        for (var i = 0; i < req.Records.Count; i++)
        {
            var activityId = req.Records[i].ActivityId;
            if (!checkedIds.Add(activityId) || await ownership.OwnsActivityAsync(dbContext, userId, activityId, ct))
                continue;

            ValidationFailures.Add(new ValidationFailure(
                $"{nameof(req.Records)}[{i}].{nameof(ActivityHistorySessionRecordRequest.ActivityId)}", "Activity not found."));
            await Send.ErrorsAsync(404, ct);
            return;
        }

        // The item links, checked as ActivityHistoryReferencedIds checks them on the single create: neither carries a
        // navigation, so nothing else stops a session from crediting its time to a stranger's to-do item.
        var linkFailure = await FirstUnownedLinkAsync(req, userId, ct);
        if (linkFailure is not null)
        {
            ValidationFailures.Add(linkFailure);
            await Send.ErrorsAsync(404, ct);
            return;
        }

        var session = req.ToEntity(userId);

        try
        {
            dbContext.Set<ActivityHistorySession>().Add(session);
            await dbContext.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // Mapped exactly as the single-row create maps it — a record colliding with an existing row on
            // (UserId, ActivityId, StartTimestamp), i.e. a double-submitted save, is a 409.
            var result = DbUtils.HandleException(ex, "Create");
            AddError(result.ErrorMessage!);
            await Send.ErrorsAsync(this.ErrorStatus(result.ErrorType), ct);
            return;
        }

        await Send.ResponseAsync(new CreateActivityHistorySessionResponse
        {
            SessionId = session.Id,
            // ToEntity keeps the request's order, and a List navigation preserves it through the save.
            RecordIds = session.Records.Select(r => r.Id).ToList()
        }, 201, ct);
    }

    private async Task<ValidationFailure?> FirstUnownedLinkAsync(CreateActivityHistorySessionRequest req, long userId, CancellationToken ct)
    {
        if (req.TodoListItemId is { } todoListItemId
            && !await ownership.OwnsReferenceAsync(dbContext, UserOwnedReferenceKeys.TodoListItem, userId, todoListItemId, ct))
            return new ValidationFailure(nameof(req.TodoListItemId), "To-do item not found.");

        if (req.RoutineTodoListId is { } routineTodoListId
            && !await ownership.OwnsReferenceAsync(dbContext, UserOwnedReferenceKeys.RoutineTodoList, userId, routineTodoListId, ct))
            return new ValidationFailure(nameof(req.RoutineTodoListId), "Routine item not found.");

        return null;
    }
}
