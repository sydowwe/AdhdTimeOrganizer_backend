using AdhdTimeOrganizer.Core.application.dto.request.activity;
using AdhdTimeOrganizer.Core.application.dto.response.activity;
using AdhdTimeOrganizer.Core.application.validator;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.Core.domain.serviceContract;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.domain.helper;
using Sydowwe.Framework.infrastructure.persistence;

namespace AdhdTimeOrganizer.Core.application.endpoint.activity.activity.command;

/// <summary>
/// Folds several duplicate activities into one: every row that pointed at a merged activity now points
/// at the survivor, and the merged activities are deleted.
/// </summary>
/// <remarks>
/// <para>
/// <b>The merged activities are deleted, not archived.</b> They disappear from the settings table
/// entirely, which is what the confirmation dialog's "the other activities are gone" already promises.
/// Archiving them instead would leave the user with rows that are invisible in pickers, still present in
/// the Archived view, and carrying nothing — the merge having already emptied them — which is a worse
/// answer than a delete to a question the user asked explicitly.
/// </para>
/// <para>
/// <b>Cross-role and cross-category merges are permitted, and the survivor wins.</b> The dialog shows
/// each candidate's role and category precisely so that choice is visible, and rejecting the cross-role
/// case would block the most common real duplicate — the same activity quick-created from two dialogs
/// that defaulted to different roles. The merged rows' placement is discarded along with the rows.
/// </para>
/// <para>
/// <b>An archived survivor is allowed.</b> The user selected it from the All view, so the merged history
/// landing on an activity that is invisible in every picker is what they asked for, and it is one
/// restore away from being visible again. Rejecting it would mean the only way to consolidate into a
/// retired activity is to restore it, merge, and re-archive.
/// </para>
/// <para>
/// <b>Atomic.</b> Everything runs inside one explicit transaction: a partially applied merge would leave
/// history split across an activity that no longer exists in any picker and one that does, with no way
/// for the user to see it or to finish the job. That is also why <c>IActivityReferenceSource</c>
/// implementations must not call <c>SaveChanges</c>.
/// </para>
/// <para>
/// ⚠ <b>Order is load-bearing.</b> Repoint first, delete second. Every activity FK in the solution is
/// <c>DeleteBehavior.Cascade</c> — except <c>TodoListItem.PairedLeisureActivityId</c>, which is
/// <c>SetNull</c> — so deleting a merged activity before its rows have moved destroys them silently:
/// no exception, no log line, and the response would still report a plausible-looking count. That
/// inventory is not a claim to be taken on trust; <c>ActivityForeignKeyInventoryTests</c> freezes the
/// whole set of activity FKs and their delete behaviours, because it has been quietly false before.
/// A slice missing from <c>ModuleServiceExtensions.ModuleAssemblies</c> produces exactly that outcome
/// for its own tables, which is why <c>SeamWiringTests</c> pins source coverage and
/// <c>ActivityMergeTests</c> asserts on surviving rows in every slice rather than on the returned counts.
/// </para>
/// </remarks>
public class MergeActivityEndpoint(
    DbContext dbContext,
    IActivityReferenceService referenceService) : Endpoint<MergeActivityRequest, MergeActivityResponse>
{
    protected virtual string[] AllowedRoles() => this.GetDefaultRoles();

    public override void Configure()
    {
        Post("/activity/merge");
        Roles(AllowedRoles());
        Validator<MergeActivityValidator>();
        Summary(s =>
        {
            s.Summary = "Merge duplicate activities";
            s.Description = "Repoints every row referencing the merged activities onto the survivor, then deletes them. All or nothing.";
            s.Response<MergeActivityResponse>(200, "Merged");
            s.Response(400, "Empty mergedIds, or the survivor is also in mergedIds");
            s.Response(404, "The survivor or one of the merged ids does not exist for this caller");
        });
    }

    public override async Task HandleAsync(MergeActivityRequest req, CancellationToken ct)
    {
        var mergedIds = req.MergedIds.Distinct().ToList();

        // Loaded through AppDbContext's global IEntityWithUser filter, so an id belonging to another
        // account simply does not come back. That makes the cross-user case a 404 rather than a 403 —
        // deliberately, so the response never confirms that a foreign id is real.
        var survivor = await dbContext.Set<Activity>().FirstOrDefaultAsync(a => a.Id == req.SurvivorId, ct);
        if (survivor == null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var merged = await dbContext.Set<Activity>().Where(a => mergedIds.Contains(a.Id)).ToListAsync(ct);
        if (merged.Count != mergedIds.Count)
        {
            // The whole merge fails; no partial. One unknown id means the user's mental model of the set
            // is wrong, and a merge is not something to half-apply while they work out which one.
            await Send.NotFoundAsync(ct);
            return;
        }

        try
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(ct);

            var repointedCount = await referenceService.RepointAsync(dbContext, survivor.Id, mergedIds, ct);

            // Saved before the delete rather than in one SaveChanges: the repoints must be in the database
            // ahead of the cascade, and interleaving them in a single change set leaves the order to EF's
            // own graph sort rather than to this method.
            await dbContext.SaveChangesAsync(ct);

            dbContext.Set<Activity>().RemoveRange(merged);
            await dbContext.SaveChangesAsync(ct);

            await transaction.CommitAsync(ct);

            await Send.OkAsync(new MergeActivityResponse
            {
                SurvivorId = survivor.Id,
                MergedCount = merged.Count,
                RepointedCount = repointedCount
            }, ct);
        }
        catch (Exception ex)
        {
            var result = DbUtils.HandleException(ex, nameof(HandleAsync));
            AddError(result.ErrorMessage!);
            await Send.ErrorsAsync(EndpointHelper.ToStatusCode(result.ErrorType), ct);
        }
    }
}
