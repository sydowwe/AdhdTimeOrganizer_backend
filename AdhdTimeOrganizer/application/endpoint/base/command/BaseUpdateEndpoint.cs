using AdhdTimeOrganizer.application.dto.request.@interface;
using AdhdTimeOrganizer.application.helper;
using AdhdTimeOrganizer.application.mapper.@interface;
using AdhdTimeOrganizer.domain.model.entityInterface;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Humanizer;

namespace AdhdTimeOrganizer.application.endpoint.@base.command;

public abstract class BaseUpdateEndpoint<TEntity, TRequest, TMapper>(
    AppCommandDbContext dbContext,
    TMapper mapper) : Endpoint<TRequest, long>
    where TEntity : class, IEntityWithId
    where TRequest : class, IUpdateRequest
    where TMapper : IBaseUpdateMapper<TEntity, TRequest>
{
    private readonly TMapper _mapper = mapper;

    public virtual string[] AllowedRoles()
    {
        return EndpointHelper.GetUserOrHigherRoles();
    }

    public override void Configure()
    {
        var entityName = typeof(TEntity).Name;
        Put($"/{entityName.Kebaberize()}/{{id}}");
        Roles(AllowedRoles());
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
                await SendNotFoundAsync(ct);
                return;
            }

            _mapper.UpdateEntity(req, entity);

            await AfterMapping(entity, req, ct);

            dbContext.Set<TEntity>().Update(entity);
            var affectedRows = await dbContext.SaveChangesAsync(ct);
            if (affectedRows == 0)
            {
                AddError("No rows were updated. Entity may not exist.");
                await SendErrorsAsync(400, ct);
                return;
            }

            await SendNoContentAsync(ct);
        }
        catch (Exception ex)
        {
            var result = DbUtils.HandleException(ex, nameof(HandleAsync));
            AddError(result.ErrorMessage!);
            await SendErrorsAsync(400, ct);
        }
    }

    protected virtual Task AfterMapping(TEntity entity, TRequest req, CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }
}