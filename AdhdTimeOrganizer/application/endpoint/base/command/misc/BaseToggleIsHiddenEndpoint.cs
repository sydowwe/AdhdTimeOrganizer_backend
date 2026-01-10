using AdhdTimeOrganizer.application.dto.request.generic;
using AdhdTimeOrganizer.domain.helper;
using AdhdTimeOrganizer.domain.model.entity.@base;
using AdhdTimeOrganizer.domain.model.entity.user;
using AdhdTimeOrganizer.domain.model.entityInterface;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Humanizer;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning;

public class BaseToggleIsHiddenEndpoint<TEntity>(AppCommandDbContext dbContext) : Endpoint<IdListRequest>
    where TEntity : class, IEntityWithId, IEntityWithIsHidden
{
    public virtual string[] AllowedRoles()
    {
        return EndpointHelper.GetUserOrHigherRoles();
    }

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
        var entities = await dbContext.Set<TEntity>().Where(e => request.Ids.Contains(e.Id)).ToListAsync(ct);

        if (entities.Count < 1)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        foreach (var entity in entities)
        {
            entity.IsHidden = !entity.IsHidden;
        }

        dbContext.Set<TEntity>().UpdateRange(entities);
        await dbContext.SaveChangesAsync(ct);

        await SendNoContentAsync(ct);
    }
}