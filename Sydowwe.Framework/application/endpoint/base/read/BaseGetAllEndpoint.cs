using FastEndpoints;
using Humanizer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sydowwe.Framework.application.dto.response;
using Sydowwe.Framework.application.dto.response.@base;
using Sydowwe.Framework.domain.entityInterface;
using Sydowwe.Framework.domain.helper;

namespace Sydowwe.Framework.application.endpoint.@base.read;

public abstract class BaseGetAllEndpoint<TEntity, TResponse>(DbContext dbContext) : EndpointWithoutRequest<List<TResponse>>
    where TEntity : class, IEntityWithId
    where TResponse : class, IIdResponse, IProjectionResponse<TResponse, TEntity>
{
    protected virtual string[] AllowedRoles() => this.GetDefaultRoles();

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

    /// <summary>
    /// Override when a response field can only be computed in C# (not in SQL) — e.g. overlaying values resolved
    /// from another module through a Kernel query onto the projected rows.
    /// </summary>
    protected virtual List<TResponse> PostProcessItems(List<TResponse> items) => items;

    /// <summary>
    /// Async variant of <see cref="PostProcessItems"/>, run before the list is returned. Default delegates to the
    /// synchronous hook. Override when the post-processing needs an awaitable call — e.g. overlaying values
    /// resolved from another module through a Kernel query.
    /// </summary>
    protected virtual Task<List<TResponse>> PostProcessItemsAsync(List<TResponse> items, CancellationToken ct) => Task.FromResult(PostProcessItems(items));

    public override async Task HandleAsync(CancellationToken ct)
    {
        try
        {
            var query = dbContext.Set<TEntity>().AsNoTracking();
            query = Filter(query);
            query = Sort(query);

            var items = await TResponse.Projection(query).ToListAsync(ct);
            items = await PostProcessItemsAsync(items, ct);
            await Send.OkAsync(items, ct);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error retrieving all {Entity}", typeof(TEntity).Name);
            AddError("An internal error occurred.");
            await Send.ErrorsAsync(500, ct);
        }
    }

    protected virtual IQueryable<TEntity> Sort(IQueryable<TEntity> query)
    {
        return query.OrderBy(e => e.Id);
    }

    protected virtual IQueryable<TEntity> Filter(IQueryable<TEntity> query) => query;
}