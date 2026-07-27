using FastEndpoints;
using Humanizer;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.dto.request.generic;
using Sydowwe.Framework.domain.entityInterface;
using Sydowwe.Framework.domain.helper;
using Sydowwe.Framework.infrastructure.persistence;

namespace Sydowwe.Framework.application.endpoint.@base.command.misc;

public abstract class BaseToggleIsHiddenEndpoint<TEntity>(DbContext dbContext) : Endpoint<IdListRequest>
    where TEntity : class, IEntityWithId, IEntityWithIsHidden
{
    public virtual string[] AllowedRoles() => IEndpoint.GetUserRole();

    public override void Configure()
    {
        var entityName = typeof(TEntity).Name;

        Patch($"{entityName.Kebaberize()}/toggle-is-hidden");
        Roles(AllowedRoles());
        Summary(s =>
        {
            s.Summary = $"Toggles {entityName} IsHidden status";
            s.Description = $"Toggles {entityName} IsHidden status";
            s.Response(204, "Toggled");
            s.Response(400, "Bad request");
        });
    }

    public override async Task HandleAsync(IdListRequest request, CancellationToken ct)
    {
        try
        {
            var ids = request.Ids.Distinct().ToList();
            if (ids.Count == 0)
            {
                AddError("No ids were provided.");
                await Send.ErrorsAsync(400, ct);
                return;
            }

            var entities = await dbContext.Set<TEntity>().Where(e => ids.Contains(e.Id)).ToListAsync(ct);

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

            foreach (var entity in entities)
                entity.IsHidden = !entity.IsHidden;

            dbContext.Set<TEntity>().UpdateRange(entities);
            await dbContext.SaveChangesAsync(ct);

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
    /// Post-fetch ownership/authorization check, invoked per entity (e.g. IDOR / "is this row mine?").
    /// Return <c>false</c> for any entity to respond 403 for the whole batch. Default allows everyone
    /// the role check already let through.
    /// </summary>
    protected virtual Task<bool> AuthorizeAsync(TEntity entity, CancellationToken ct = default) => Task.FromResult(true);
}