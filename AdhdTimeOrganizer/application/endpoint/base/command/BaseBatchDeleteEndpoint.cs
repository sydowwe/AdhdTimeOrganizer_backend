using AdhdTimeOrganizer.application.dto.request.generic;
using AdhdTimeOrganizer.domain.helper;
using AdhdTimeOrganizer.domain.model.entityInterface;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Humanizer;

namespace AdhdTimeOrganizer.application.endpoint.@base.command;

public abstract class BaseBatchDeleteEndpoint<TEntity>(AppCommandDbContext dbContext) : Endpoint<IdListRequest>
    where TEntity : class, IEntityWithId
{
    protected virtual string[] AllowedRoles()
    {
        return EndpointHelper.GetAdminOrHigherRoles();
    }

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
            var entities = new List<TEntity>();
            foreach (var idRequest in req.Ids)
            {
                var entity = await dbContext.Set<TEntity>().FindAsync([idRequest], ct);
                if (entity == null)
                {
                    await SendNotFoundAsync(ct);
                    return;
                }
                entities.Add(entity);
            }

            dbContext.Set<TEntity>().RemoveRange(entities);
            await dbContext.SaveChangesAsync(ct);

            await SendNoContentAsync(ct);
        }
        catch (Exception ex)
        {
            var result = DbUtils.HandleException(ex, nameof(HandleAsync));
            AddError(result.ErrorMessage!);
            await SendErrorsAsync(400, ct);
        }
    }
}
