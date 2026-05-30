using AdhdTimeOrganizer.application.dto.response.generic;
using AdhdTimeOrganizer.application.helper;
using AdhdTimeOrganizer.domain.model.entityInterface;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Humanizer;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.@base.read;

public abstract class BaseGetSelectOptionsEndpoint<TEntity>(AppDbContext dbContext) : EndpointWithoutRequest<List<SelectOptionResponse>>
    where TEntity : class, IEntityWithId
{
    protected virtual string[] AllowedRoles() => EndpointHelper.GetAdminOrHigherRoles();

    protected virtual string AddedRouteParam => string.Empty;

    public override void Configure()
    {
        var entityName = typeof(TEntity).Name;
        Get($"/{entityName.Kebaberize()}/all-options/{AddedRouteParam}");
        Roles(AllowedRoles());
        Summary(s =>
        {
            s.Summary = $"Get {entityName} select options";
            s.Description = $"Retrieves {entityName} as select options (id and text)";
            s.Response<List<SelectOptionResponse>>(200, "Success");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var query = dbContext.Set<TEntity>().AsNoTracking();

        query = Filter(query);
        query = Sort(query);

        var options = await Map(query).ToListAsync(ct);

        await Send.OkAsync(options, ct);
    }

    protected virtual IQueryable<TEntity> Filter(IQueryable<TEntity> query) => query;

    protected virtual IQueryable<TEntity> Sort(IQueryable<TEntity> query) => query.OrderBy(e => e.Id);

    protected abstract IQueryable<SelectOptionResponse> Map(IQueryable<TEntity> query);
}