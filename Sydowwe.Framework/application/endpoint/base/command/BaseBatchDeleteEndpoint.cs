using FastEndpoints;
using Humanizer;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.dto.request.generic;
using Sydowwe.Framework.domain.entityInterface;
using Sydowwe.Framework.domain.helper;
using Sydowwe.Framework.infrastructure.persistence;

namespace Sydowwe.Framework.application.endpoint.@base.command;

public abstract class BaseBatchDeleteEndpoint<TEntity>(DbContext dbContext) : Endpoint<IdListRequest>
    where TEntity : class, IEntityWithId
{
    public virtual string[] AllowedRoles() => IEndpoint.GetUserRole();

    public override void Configure()
    {
        var entityName = typeof(TEntity).Name;
        Post($"/{entityName.Kebaberize()}/batch-delete");
        Roles(AllowedRoles());
        Summary(s =>
        {
            s.Summary = $"Batch delete {entityName}";
            s.Description = $"Deletes multiple {entityName} entities";
            s.Response(204, "Success");
            s.Response(404, "One or more entities not found");
        });
    }

    public override async Task HandleAsync(IdListRequest req, CancellationToken ct)
    {
        try
        {
            var ids = req.Ids.Distinct().ToList();
            if (ids.Count == 0)
            {
                AddError("No ids were provided.");
                await Send.ErrorsAsync(400, ct);
                return;
            }

            var entities = await dbContext.Set<TEntity>()
                .Where(e => ids.Contains(e.Id))
                .ToListAsync(ct);

            if (entities.Count != ids.Count)
            {
                await Send.NotFoundAsync(ct);
                return;
            }

            foreach (var entity in entities)
                if (!await AuthorizeAsync(entity, ct))
                {
                    await Send.ForbiddenAsync(ct);
                    return;
                }

            await BeforeDeleteAsync(entities, ct);

            dbContext.Set<TEntity>().RemoveRange(entities);
            await dbContext.SaveChangesAsync(ct);

            await AfterSave(entities, ct);

            await Send.NoContentAsync(ct);
        }
        catch (Exception ex)
        {
            var result = DbUtils.HandleException(ex, nameof(HandleAsync));
            AddError(result.ErrorMessage!);
            await Send.ErrorsAsync(EndpointHelper.ToStatusCode(result.ErrorType), ct);
        }
    }

    /// <summary>
    /// Post-fetch ownership/authorization check, invoked per entity in the batch (e.g. IDOR /
    /// "is this row mine?"). Return <c>false</c> for any entity to respond 403 for the whole batch.
    /// Default allows everyone the role check already let through.
    /// </summary>
    protected virtual Task<bool> AuthorizeAsync(TEntity entity, CancellationToken ct = default) => Task.FromResult(true);

    /// <summary>
    /// Runs after authorization but <b>before</b> the rows are removed — the only place to read state the
    /// delete (or a cascade off it) is about to destroy. Batch counterpart of the single-delete hook; pair it
    /// with <see cref="AfterSave"/>.
    /// </summary>
    protected virtual Task BeforeDeleteAsync(IReadOnlyList<TEntity> entities, CancellationToken ct = default) => Task.CompletedTask;

    /// <summary>Runs once the batch delete commits. Use for effects that must not happen if it failed.</summary>
    protected virtual Task AfterSave(IReadOnlyList<TEntity> entities, CancellationToken ct = default) => Task.CompletedTask;
}