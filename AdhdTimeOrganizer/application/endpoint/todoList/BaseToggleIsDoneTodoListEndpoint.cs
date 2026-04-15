using AdhdTimeOrganizer.application.dto.request.generic;
using AdhdTimeOrganizer.application.helper;
using AdhdTimeOrganizer.domain.model.entity;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Humanizer;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.todoList;

public abstract class BaseToggleIsDoneTodoListEndpoint<TEntity>(AppDbContext dbContext) : Endpoint<ToggleIsDoneRequest>
    where TEntity : BaseTodoListItem
{
    public virtual string[] AllowedRoles() => EndpointHelper.GetUserOrHigherRoles();

    public override void Configure()
    {
        var entityName = typeof(TEntity).Name;
        Patch($"{entityName.Kebaberize()}/toggle-is-done");
        Roles(AllowedRoles());
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
        var itemsToToggle = await FetchAndPrepare(request.Ids, now, ct);

        if (itemsToToggle.Count == 0)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        foreach (var entity in itemsToToggle)
        {
            IsDoneLogic(entity, request.ForceValue);
            AfterItemToggled(entity, now);
        }

        dbContext.Set<TEntity>().UpdateRange(itemsToToggle);
        await dbContext.SaveChangesAsync(ct);

        foreach (var entity in itemsToToggle)
            await PublishEvent(entity, ct);

        await SendNoContentAsync(ct);
    }

    /// <summary>
    /// Loads entities for the given IDs and applies any pre-toggle logic (e.g. period reset).
    /// Returns only the items that should be toggled.
    /// </summary>
    protected abstract Task<List<TEntity>> FetchAndPrepare(ICollection<long> ids, DateTime now, CancellationToken ct);

    protected virtual void AfterItemToggled(TEntity entity, DateTime now)
    {
    }

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
                }
                else
                {
                    entity.DoneCount = (entity.DoneCount ?? 0) + 1;
                    if (entity.DoneCount == entity.TotalCount)
                        entity.IsDone = true;
                    ResetSteps(entity);
                }
            }
        }
        else
        {
            entity.IsDone = forceValue ?? !entity.IsDone;
            if (entity.Steps.Count != 0)
            {
                foreach (var step in entity.Steps)
                    step.IsDone = entity.IsDone;
            }
        }
    }

    private static void ResetSteps(TEntity entity)
    {
        foreach (var step in entity.Steps)
            step.IsDone = entity.IsDone;
    }
}