using FastEndpoints;
using Humanizer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sydowwe.Framework.application.dto.response.generic;
using Sydowwe.Framework.domain.entityInterface;
using Sydowwe.Framework.domain.helper;

namespace Sydowwe.Framework.application.endpoint.@base.read;

public abstract class BaseGetSelectOptionsEndpoint<TEntity>(DbContext dbContext) : EndpointWithoutRequest<List<SelectOptionResponse>>
    where TEntity : class, IEntityWithId
{
    protected virtual string[] AllowedRoles() => this.GetDefaultRoles();

    public virtual string AddedRouteParam => string.Empty;

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
        try
        {
            var query = dbContext.Set<TEntity>().AsNoTracking();

            query = Filter(query);
            query = Sort(query);

            var options = await Map(query).ToListAsync(ct);

            await Send.OkAsync(options, ct);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error retrieving select options for {Entity}", typeof(TEntity).Name);
            AddError("An internal error occurred.");
            await Send.ErrorsAsync(500, ct);
        }
    }

    public virtual IQueryable<TEntity> Filter(IQueryable<TEntity> query) => query;

    protected virtual IQueryable<TEntity> Sort(IQueryable<TEntity> query)
    {
        return query.OrderBy(e => e.Id);
    }

    protected abstract IQueryable<SelectOptionResponse> Map(IQueryable<TEntity> query);
}