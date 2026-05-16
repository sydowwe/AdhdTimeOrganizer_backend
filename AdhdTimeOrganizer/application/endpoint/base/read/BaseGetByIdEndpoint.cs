using AdhdTimeOrganizer.application.dto.response.@base;
using AdhdTimeOrganizer.application.helper;
using AdhdTimeOrganizer.application.mapper.@interface;
using AdhdTimeOrganizer.domain.model.entityInterface;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Humanizer;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.@base.read;

public abstract class BaseGetByIdEndpoint<TEntity, TResponse, TMapper>(AppDbContext dbContext, TMapper mapper) : EndpointWithoutRequest<TResponse>
    where TEntity : class, IEntityWithId
    where TResponse : class, IIdResponse
    where TMapper : IBaseResponseMapper<TEntity, TResponse>
{
    private readonly TMapper _mapper = mapper;



    public override void Configure()
    {
        var entityName = typeof(TEntity).Name;
        Get($"/{entityName.Kebaberize()}/{{id:long:required}}");

        Summary(s =>
        {
            s.Summary = $"Get {entityName} by ID";
            s.Description = $"Retrieves a specific {entityName} by its ID";
            s.Response<TResponse>(200, "Success");
            s.Response(404, "Not found");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<long>("id");
        var dbSet = dbContext.Set<TEntity>();
        var query = WithIncludes(dbSet);

        var entity = await query.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (entity == null)
        {
            AddError($"{typeof(TEntity).Name} not found.");
            await Send.ErrorsAsync(404, ct);
            return;
        }

        var response = _mapper.ToResponse(entity);
        await Send.OkAsync(response, ct);
    }

    protected virtual IQueryable<TEntity> WithIncludes(IQueryable<TEntity> query) => query;
}