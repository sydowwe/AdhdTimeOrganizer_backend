using AdhdTimeOrganizer.Core.application.dto.request.generic;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using FastEndpoints;
using Humanizer;
using Sydowwe.Framework.application.error;
using Sydowwe.Framework.domain.helper;
using Sydowwe.Framework.infrastructure.persistence;

namespace AdhdTimeOrganizer.TodoLists.application.endpoint.todoList;

public abstract class BaseToggleIsDoneTodoListEndpoint<TEntity>(DbContext dbContext) : Endpoint<ToggleIsDoneRequest>
    where TEntity : BaseTodoListItem
{
    public override void Configure()
    {
        var entityName = typeof(TEntity).Name;
        Patch($"/{entityName.Kebaberize()}/toggle-is-done");

        Summary(s =>
        {
            s.Summary = $"Toggles {entityName} IsDone status";
            s.Description = $"Toggles {entityName} IsDone status";
            s.Response(204, "Toggled");
            s.Response(400, "Bad request");
        });
    }

    public override async Task HandleAsync(ToggleIsDoneRequest request, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var requestedIds = request.Ids.Distinct().ToList();
        var itemsToToggle = await FetchAndPrepare(requestedIds, now, ct);

        // Every requested id has to resolve, not merely one of them. Both subclasses return exactly the subset
        // they could load, and an id belonging to another user matches nothing under the global filter — so the
        // old "empty means 404" test answered 204 for a batch that was only partly applied, which the client
        // cannot tell from a complete one. "Uncheck all" sends the whole done set at once, so this is the
        // request most likely to carry a stale id.
        if (requestedIds.Count == 0 || itemsToToggle.Count != requestedIds.Count)
        {
            AddError($"{typeof(TEntity).Name} not found.");
            await Send.ErrorsAsync(404, ct);
            return;
        }

        foreach (var entity in itemsToToggle)
        {
            IsDoneLogic(entity, request.ForceValue);
            AfterItemToggled(entity, now);
        }

        // Through the Result helper, not a bare SaveChangesAsync: every entity here carries a row_version, so
        // two clients toggling the same item is an ordinary DbUpdateConcurrencyException, and Routines' override
        // adds a period whose reset can collide besides. Unmapped, either escapes as a 500 indistinguishable
        // from a real fault; mapped, the loser gets the 409 the rest of the API returns (CQ-10).
        //
        // SaveTrackedChangesAsync rather than UpdateRangeAsync: FetchAndPrepare loads these items tracked, so
        // UpdateRange would re-mark every property modified and write the whole row — steps JSON and all — to
        // change one flag and one counter.
        var result = await dbContext.SaveTrackedChangesAsync(ct);
        if (result.Failed)
        {
            AddError(result.ErrorMessage ?? "An error occurred while toggling.");
            await Send.ErrorsAsync(this.ErrorStatus(result.ErrorType), ct);
            return;
        }

        // Only past the failure gate: AfterSave announces something that has happened, and PublishEvent below
        // drives the cross-slice fan-out. Both on a save that did not commit would report a lie.
        await AfterSave(ct);

        foreach (var entity in itemsToToggle)
            await PublishEvent(entity, ct);

        await Send.NoContentAsync(ct);
    }

    /// <summary>
    /// Loads entities for the given IDs and applies any pre-toggle logic (e.g. period reset).
    /// Returns only the items that should be toggled.
    /// </summary>
    protected abstract Task<List<TEntity>> FetchAndPrepare(ICollection<long> ids, DateTime now, CancellationToken ct);

    protected virtual void AfterItemToggled(TEntity entity, DateTime now)
    {
    }

    /// <summary>
    /// Runs once after the toggle has been committed. For side effects that must not be able to roll the
    /// toggle back — announcing something that has already happened, rather than changing what happens.
    /// <see cref="FetchAndPrepare"/> is the place to record what such a hook needs to act on.
    /// </summary>
    protected virtual Task AfterSave(CancellationToken ct) => Task.CompletedTask;

    protected abstract Task PublishEvent(TEntity entity, CancellationToken ct);

    protected void IsDoneLogic(TEntity entity, bool? forceValue = null)
    {
        if (entity.TotalCount.HasValue)
        {
            if (forceValue == false)
            {
                if (entity.IsDone)
                {
                    entity.DoneCount = entity.TotalCount - 1;
                    entity.IsDone = false;
                }
                else if (entity.DoneCount > 0)
                {
                    entity.DoneCount--;
                }

                ResetSteps(entity);
            }
            else if (forceValue == true)
            {
                entity.DoneCount = (entity.DoneCount ?? 0) + 1;
                if (entity.DoneCount >= entity.TotalCount)
                {
                    entity.DoneCount = entity.TotalCount;
                    entity.IsDone = true;
                }

                ResetSteps(entity);
            }
            else
            {
                if (entity.IsDone)
                {
                    entity.DoneCount = 0;
                    entity.IsDone = false;
                    ResetSteps(entity);
                }
                else
                {
                    entity.DoneCount = (entity.DoneCount ?? 0) + 1;
                    // >=, not ==, and clamped — matching the forced arm above and ApplyStepToggle. An item
                    // whose DoneCount has overshot its TotalCount would otherwise never tick to done; it
                    // would just keep incrementing, one increment further out of range on every tap.
                    if (entity.DoneCount >= entity.TotalCount)
                    {
                        entity.DoneCount = entity.TotalCount;
                        entity.IsDone = true;
                    }

                    ResetSteps(entity);
                }
            }
        }
        else
        {
            entity.IsDone = forceValue ?? !entity.IsDone;
            if (entity.Steps.Count != 0)
                foreach (var step in entity.Steps)
                    step.IsDone = entity.IsDone;
        }
    }

    private static void ResetSteps(TEntity entity)
    {
        foreach (var step in entity.Steps)
            step.IsDone = entity.IsDone;
    }
}