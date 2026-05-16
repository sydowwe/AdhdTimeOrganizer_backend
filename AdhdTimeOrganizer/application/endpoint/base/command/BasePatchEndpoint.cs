using AdhdTimeOrganizer.application.dto.request.@interface;
using AdhdTimeOrganizer.application.dto.response.@base;
using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.application.helper;
using AdhdTimeOrganizer.domain.model.entity.user;
using AdhdTimeOrganizer.domain.model.entityInterface;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Humanizer;

namespace AdhdTimeOrganizer.application.endpoint.@base.command;

public abstract class BasePatchEndpoint<TEntity, TRequest, TResponse>(
    AppDbContext dbContext) : Endpoint<TRequest>
    where TEntity : class, IEntityWithId
    where TRequest : class, IPatchRequest
    where TResponse : class, IIdResponse
{


    public override void Configure()
    {
        var entityName = typeof(TEntity).Name;
        Patch($"/{entityName.Kebaberize()}/{{id:long:required}}");
        
        Summary(s =>
        {
            s.Summary = $"Patch {entityName}";
            s.Description = $"Partially updates an existing {entityName}";
            s.Response(204, "Success");
            s.Response(404, "Not found");
            s.Response(400, "Bad request");
        });
    }

    public override async Task HandleAsync(TRequest req, CancellationToken ct)
    {
        try
        {
            var entity = await dbContext.Set<TEntity>().FindAsync([Route<long>("id")], ct);
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

            Mapping(entity, req);

            dbContext.Set<TEntity>().Update(entity);
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

    protected abstract void Mapping(TEntity entity, TRequest req);
}