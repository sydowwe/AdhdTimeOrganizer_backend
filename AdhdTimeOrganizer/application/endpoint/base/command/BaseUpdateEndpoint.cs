using AdhdTimeOrganizer.application.dto.request.@interface;
using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.application.helper;
using AdhdTimeOrganizer.application.mapper.@interface;
using AdhdTimeOrganizer.domain.model.entity.user;
using AdhdTimeOrganizer.domain.model.entityInterface;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Humanizer;

namespace AdhdTimeOrganizer.application.endpoint.@base.command;

public abstract class BaseUpdateEndpoint<TEntity, TRequest, TMapper>(
    AppDbContext dbContext,
    TMapper mapper) : Endpoint<TRequest, long>
    where TEntity : class, IEntityWithId
    where TRequest : class, IUpdateRequest
    where TMapper : IBaseUpdateMapper<TEntity, TRequest>
{
    private readonly TMapper _mapper = mapper;


    public virtual string Route => typeof(TEntity).Name.Kebaberize();

    public override void Configure()
    {
        var entityName = typeof(TEntity).Name;
        Put($"/{Route}" + "/{id:long:required}");
        
        Summary(s =>
        {
            s.Summary = $"Update {entityName}";
            s.Description = $"Updates an existing {entityName}";
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

            _mapper.UpdateEntity(req, entity);

            await AfterMapping(entity, req, ct);

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

    protected virtual Task AfterMapping(TEntity entity, TRequest req, CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }
}