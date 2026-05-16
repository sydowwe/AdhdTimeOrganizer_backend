using AdhdTimeOrganizer.application.dto.request.generic;
using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.application.helper;
using AdhdTimeOrganizer.domain.model.entity.user;
using AdhdTimeOrganizer.domain.model.entityInterface;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Humanizer;

namespace AdhdTimeOrganizer.application.endpoint.@base.command;

public abstract class BaseBatchDeleteEndpoint<TEntity>(AppDbContext dbContext) : Endpoint<IdListRequest>
    where TEntity : class, IEntityWithId
{
    public override void Configure()
    {
        var entityName = typeof(TEntity).Name;
        Post($"/{entityName.Kebaberize()}/batch-delete");
        
        Summary(s =>
        {
            s.Summary = $"Batch delete {entityName}";
            s.Description = $"Deletes multiple {entityName} entities";
            s.Response(204, "Success");
            s.Response(404, "One or more entities not found");
            s.Response(400, "Bad request");
        });
    }

    public override async Task HandleAsync(IdListRequest req, CancellationToken ct)
    {
        try
        {
            var entities = new List<TEntity>();
            foreach (var idRequest in req.Ids)
            {
                var entity = await dbContext.Set<TEntity>().FindAsync([idRequest], ct);
                if (entity == null)
                {
                    AddError($"{typeof(TEntity).Name} not found.");
                    await Send.ErrorsAsync(404, ct);
                    return;
                }

                if (entity is IEntityWithUser entityWithUser && entityWithUser.UserId != User.GetId())
                {
                    AddError($"{typeof(TEntity).Name} not found.");
                    await Send.ErrorsAsync(404, ct);
                    return;
                }

                entities.Add(entity);
            }

            dbContext.Set<TEntity>().RemoveRange(entities);
            await dbContext.SaveChangesAsync(ct);

            await Send.NoContentAsync(ct);
        }
        catch (Exception ex)
        {
            var result = DbUtils.HandleException(ex, nameof(HandleAsync));
            AddError(result.ErrorMessage!);
            await Send.ErrorsAsync(400, ct);
        }
    }
}
