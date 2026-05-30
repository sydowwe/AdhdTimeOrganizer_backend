using AdhdTimeOrganizer.application.dto.request.@interface;
using AdhdTimeOrganizer.application.helper;
using AdhdTimeOrganizer.domain.model.entityInterface;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Humanizer;

namespace AdhdTimeOrganizer.application.endpoint.@base.command;

public abstract class BaseUpdateEndpoint<TEntity, TRequest>(AppDbContext dbContext) : Endpoint<TRequest, long>
    where TEntity : class, IEntityWithId
    where TRequest : class, IUpdateRequest<TEntity>
{
    public virtual string[] AllowedRoles() => EndpointHelper.GetAdminOrHigherRoles();
    public virtual string Route => typeof(TEntity).Name.Kebaberize();

    public override void Configure()
    {
        var entityName = typeof(TEntity).Name;
        Put(Route + "/{id:long}");
        Roles(AllowedRoles());
        Summary(s =>
        {
            s.Summary = $"Update {entityName}";
            s.Description = $"Updates an existing {entityName}";
            s.Response(200, "Success");
            s.Response(404, "Not found");
            s.Response(400, "Bad request");
        });
    }

    public override async Task HandleAsync(TRequest req, CancellationToken ct)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(ct);
        try
        {
            if (!await BeforeMapping(req, ct))
            {
                await transaction.RollbackAsync(ct);
                return;
            }

            var entity = await dbContext.Set<TEntity>().FindAsync([Route<long>("id")], ct);
            if (entity == null)
            {
                await transaction.RollbackAsync(ct);
                await Send.NotFoundAsync(ct);
                return;
            }

            await UpdateEntityAsync(entity, req, ct);

            if (!await AfterMapping(entity, req, ct))
            {
                await transaction.RollbackAsync(ct);
                return;
            }

            dbContext.Set<TEntity>().Update(entity);
            await dbContext.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            await AfterSave(entity, ct);
            await Send.OkAsync(entity.Id, ct);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(ct);
            var result = DbUtils.HandleException(ex, nameof(HandleAsync));
            AddError(result.ErrorMessage!);
            await Send.ErrorsAsync(EndpointHelper.ToStatusCode(result.ErrorType), ct);
        }
    }

    protected virtual Task<bool> BeforeMapping(TRequest req, CancellationToken ct = default)
        => Task.FromResult(true);

    protected virtual Task UpdateEntityAsync(TEntity entity, TRequest req, CancellationToken ct = default)
    {
        req.UpdateEntity(entity);
        return Task.CompletedTask;
    }

    protected virtual Task<bool> AfterMapping(TEntity entity, TRequest req, CancellationToken ct = default)
        => Task.FromResult(true);

    protected virtual Task AfterSave(TEntity entity, CancellationToken ct = default) => Task.CompletedTask;
}