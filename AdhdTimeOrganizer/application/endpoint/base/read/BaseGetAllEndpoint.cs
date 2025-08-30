using AdhdTimeOrganizer.application.dto.response.@base;
using AdhdTimeOrganizer.application.mapper.@interface;
using AdhdTimeOrganizer.domain.helper;
using AdhdTimeOrganizer.domain.model.entityInterface;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Humanizer;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.@base.read;

public abstract class BaseGetAllEndpoint<TEntity, TResponse, TMapper>(AppCommandDbContext dbContext, TMapper mapper) : EndpointWithoutRequest<List<TResponse>>
    where TEntity : class, IEntityWithId
    where TResponse : class, IIdResponse
    where TMapper : IBaseResponseMapper<TEntity, TResponse>
{
    private readonly TMapper _mapper = mapper;

    public virtual string[] AllowedRoles()
    {
        return EndpointHelper.GetUserOrHigherRoles();
    }

    public virtual string? RouteParam => null;

    private string AddedRouteParam => RouteParam != null ? $"/{RouteParam}" : string.Empty;

    public override void Configure()
    {
        var entityName = typeof(TEntity).Name;
        Get($"/{entityName.Kebaberize()}" + AddedRouteParam);
        Roles(AllowedRoles());

        Summary(s =>
        {
            s.Summary = $"Get all {entityName}";
            s.Description = $"Retrieves all {entityName} records";
            s.Response<List<TResponse>>(200, "Success");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var dbSet = dbContext.Set<TEntity>();
        var query = WithIncludes(dbSet);

        var entities = await query.ToListAsync(ct);
        var responses = entities.Select(MapToResponse).ToList();

        await SendOkAsync(responses, ct);
    }

    protected virtual IQueryable<TEntity> WithIncludes(IQueryable<TEntity> query) => query;

    protected virtual TResponse MapToResponse(TEntity entity) => _mapper.ToResponse(entity);
}