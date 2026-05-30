using AdhdTimeOrganizer.application.dto.response;
using AdhdTimeOrganizer.application.dto.response.@base;
using AdhdTimeOrganizer.application.helper;
using AdhdTimeOrganizer.domain.model.entityInterface;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Humanizer;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.@base.read;

public abstract class BaseGetAllEndpoint<TEntity, TResponse>(AppDbContext dbContext) : EndpointWithoutRequest<List<TResponse>>
    where TEntity : class, IEntityWithId
    where TResponse : class, IIdResponse, IProjectionResponse<TResponse, TEntity>
{
    public virtual string[] AllowedRoles() => EndpointHelper.GetAdminOrHigherRoles();

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
        var query = dbContext.Set<TEntity>().AsNoTracking();
        query = Filter(query);
        query = Sort(query);

        var items = await TResponse.Projection(query).ToListAsync(ct);
        await Send.OkAsync(items, ct);
    }

    protected virtual IQueryable<TEntity> Sort(IQueryable<TEntity> query) => query.OrderBy(e => e.Id);

    protected virtual IQueryable<TEntity> Filter(IQueryable<TEntity> query) => query;
}