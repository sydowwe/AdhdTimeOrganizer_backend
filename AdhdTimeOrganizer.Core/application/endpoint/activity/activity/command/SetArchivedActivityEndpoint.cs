using AdhdTimeOrganizer.Core.application.dto.request.activity;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.domain.helper;
using Sydowwe.Framework.infrastructure.persistence;

namespace AdhdTimeOrganizer.Core.application.endpoint.activity.activity.command;

/// <summary>
/// Archives or restores an activity — the lifecycle operation the app never had.
/// </summary>
/// <remarks>
/// <para>
/// <b>Route.</b> <c>PATCH /activity/{id}/archived</c> is unambiguous here. The frontend's note worried
/// about a clash with <c>/activity/{id}/Overwrite</c> and <c>/activity/{id}/Clone</c> possibly being one
/// route with a <c>{mode}</c> parameter — they are not, and they are not even <c>PATCH</c>: clone is
/// <c>POST /activity/{id}/clone</c> and the overwrite half is <c>PUT /activity/{id}/quick-edit</c>.
/// Nothing else claims <c>PATCH /activity/{id}/…</c>, so no precedence rule is needed.
/// </para>
/// <para>
/// <b>Idempotent by construction.</b> Archiving an already-archived activity is a 204, not a 409 — the
/// row action can be double-clicked and there is no useful error to show for it. The write is issued
/// unconditionally rather than skipped when the state already matches: skipping would be a marginal
/// saving and would make the two paths behave differently under the row-version concurrency check.
/// </para>
/// <para>
/// Deliberately <b>not</b> part of <c>PUT /activity/{id}</c>. <c>ActivityRequest</c> does not carry
/// <c>IsArchived</c> and must never learn to — the edit form would then un-archive on every save, the
/// same trap <c>ActivityHistoryRequest</c> documents for its two item links. A newly created activity is
/// always active.
/// </para>
/// </remarks>
public class SetArchivedActivityEndpoint(DbContext dbContext) : Endpoint<SetActivityArchivedRequest>
{
    protected virtual string[] AllowedRoles() => this.GetDefaultRoles();

    public override void Configure()
    {
        Patch("/activity/{id:long:required}/archived");
        Roles(AllowedRoles());
        Summary(s =>
        {
            s.Summary = "Archive or restore an activity";
            s.Description = "Retires an activity from every picker without touching the rows that reference it, or brings it back.";
            s.Response(204, "Applied (including when the activity was already in the requested state)");
            s.Response(404, "Not found, or not the caller's activity");
            s.Response(400, "Bad request");
        });
    }

    public override async Task HandleAsync(SetActivityArchivedRequest req, CancellationToken ct)
    {
        try
        {
            // FindAsync goes through AppDbContext's global IEntityWithUser filter, so another user's id
            // is simply not found — 404, never 403. Existence must not be confirmable across accounts.
            var entity = await dbContext.Set<Activity>().FindAsync([Route<long>("id")], ct);
            if (entity == null)
            {
                await Send.NotFoundAsync(ct);
                return;
            }

            entity.IsArchived = req.IsArchived;

            var result = await dbContext.UpdateEntityAsync(entity, ct);
            if (result.Failed)
            {
                AddError(result.ErrorMessage!);
                await Send.ErrorsAsync(400, ct);
                return;
            }

            await Send.NoContentAsync(ct);
        }
        catch (Exception ex)
        {
            var result = DbUtils.HandleException(ex, nameof(HandleAsync));
            AddError(result.ErrorMessage!);
            await Send.ErrorsAsync(EndpointHelper.ToStatusCode(result.ErrorType), ct);
        }
    }
}
