using AdhdTimeOrganizer.application.dto.response.@base;
using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.application.helper;
using AdhdTimeOrganizer.application.mapper.@interface;
using AdhdTimeOrganizer.domain.model.entity.user;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Humanizer;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.@base.read;

public abstract class BaseGetAllEndpoint<TEntity, TResponse, TMapper>(AppCommandDbContext dbContext, TMapper mapper) : EndpointWithoutRequest<List<TResponse>>
    where TEntity : class, IEntityWithUser
    where TResponse : class, IIdResponse
    where TMapper : IBaseReadMapper<TEntity, TResponse>
{

    public virtual string[] AllowedRoles()
    {
        return EndpointHelper.GetUserOrHigherRoles();
    }

    public virtual string? RouteParam => null;

    public virtual bool FilteredByUser => true;

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

        if (FilteredByUser)
        {
            query = query.FilteredByUser(User.GetId());
        }
        query = Sort(query);

        var items = await mapper.ProjectToResponse(query).ToListAsync(ct);
        await SendOkAsync(items, ct);
    }

    protected virtual IQueryable<TEntity> WithIncludes(IQueryable<TEntity> query) => query;

    protected virtual IQueryable<TEntity> Sort(IQueryable<TEntity> query) => query.OrderBy(e => e.Id);

}