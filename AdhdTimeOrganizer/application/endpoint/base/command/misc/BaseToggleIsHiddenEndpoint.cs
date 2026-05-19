using AdhdTimeOrganizer.application.dto.request.generic;
using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.application.helper;
using AdhdTimeOrganizer.domain.model.entity.user;
using AdhdTimeOrganizer.domain.model.entityInterface;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Humanizer;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.@base.command.misc;

public class BaseToggleIsHiddenEndpoint<TEntity>(AppDbContext dbContext) : Endpoint<IdListRequest>
    where TEntity : class, IEntityWithId, IEntityWithIsHidden
{
    public override void Configure()
    {
        var entityName = typeof(TEntity).Name;

        Patch($"/{entityName.Kebaberize()}/toggle-is-hidden");
        
        Summary(s =>
        {
            s.Summary = $"Toggle visibility of {entityName}";
            s.Description = $"Toggles the IsHidden status for one or more {entityName} entities (visible ↔ hidden)";
            s.Response(204, "Toggled");
            s.Response(404, "Not found");
        });
    }

    public override async Task HandleAsync(IdListRequest request, CancellationToken ct)
    {
        var entities = await dbContext.Set<TEntity>().Where(e => request.Ids.Contains(e.Id)).ToListAsync(ct);

        if (entities.Count < 1)
        {
            AddError($"{typeof(TEntity).Name} not found.");
            await Send.ErrorsAsync(404, ct);
            return;
        }

        var userId = User.GetId();
        if (entities.OfType<IEntityWithUser>().Any(e => e.UserId != userId))
        {
            AddError($"{typeof(TEntity).Name} not found.");
            await Send.ErrorsAsync(404, ct);
            return;
        }

        foreach (var entity in entities)
        {
            entity.IsHidden = !entity.IsHidden;
        }

        dbContext.Set<TEntity>().UpdateRange(entities);
        await dbContext.SaveChangesAsync(ct);

        await Send.NoContentAsync(ct);
    }
}