using AdhdTimeOrganizer.application.dto.request.generic;
using AdhdTimeOrganizer.application.helper;
using AdhdTimeOrganizer.domain.model.entityInterface;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Humanizer;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.@base.command;

public abstract class BaseBatchDeleteEndpoint<TEntity>(AppDbContext dbContext) : Endpoint<IdListRequest>
    where TEntity : class, IEntityWithId
{
    public virtual string[] AllowedRoles() => EndpointHelper.GetAdminOrHigherRoles();

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

            dbContext.Set<TEntity>().RemoveRange(entities);
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
}