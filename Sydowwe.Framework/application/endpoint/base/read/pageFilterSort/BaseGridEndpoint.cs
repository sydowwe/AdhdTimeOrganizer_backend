using FastEndpoints;
using Humanizer;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.dto.request.@base.table;
using Sydowwe.Framework.application.dto.request.generic;
using Sydowwe.Framework.application.dto.request.@interface;
using Sydowwe.Framework.application.dto.response;
using Sydowwe.Framework.application.dto.response.@base;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.domain.entityInterface;
using Sydowwe.Framework.domain.helper;
using Sydowwe.Framework.infrastructure.persistence;

namespace Sydowwe.Framework.application.endpoint.@base.read.pageFilterSort;

public abstract class BaseGridEndpoint<TEntity, TResponse, TFilter>(DbContext dbContext)
    : Endpoint<BaseFilterSortPaginateRequest<TFilter>, BaseGridResponse<TResponse>>
    where TEntity : class, IEntityWithId
    where TResponse : class, IIdResponse, IProjectionResponse<TResponse, TEntity>
    where TFilter : class, IFilterRequest
{
    protected virtual string[] AllowedRoles() => this.GetDefaultRoles();

    public virtual string EndpointPath => "filtered-table";
    public virtual string EntityName => typeof(TEntity).Name;

    /// <summary>
    /// Upper bound on <c>ItemsPerPage</c>. Requests above this are rejected with 422 to
    /// guard against unbounded queries (DoS). Override to raise/lower the cap per endpoint.
    /// </summary>
    public virtual int MaxItemsPerPage => 200;


    /// <summary>
    /// No-op by default. Override to add a WHERE clause that restricts results to the
    /// current user (e.g. by UserId or EmployeeId) — required whenever non-Admin roles
    /// can reach the endpoint, otherwise all rows leak.
    /// </summary>
    protected virtual Task<IQueryable<TEntity>> ApplyUserScoping(IQueryable<TEntity> query, long userId, CancellationToken ct = default) => Task.FromResult(query);

    /// <summary>
    /// No-op by default. Override to add a standing WHERE clause that ALWAYS runs, independent of
    /// the request — unlike <see cref="ApplyCustomFiltering"/>, which only runs when the caller
    /// sends a filter. Use for query predicates that are not user scoping, e.g. excluding
    /// soft-deleted rows (<c>Where(x =&gt; x.IsActive)</c>) or other global query filters.
    /// </summary>
    protected virtual IQueryable<TEntity> ApplyBaseFiltering(IQueryable<TEntity> query) => query;

    /// <summary>
    /// Override to remap sort keys before they reach <c>SortByMany</c>.
    /// Use when the frontend sends a key (e.g. "address") that differs from the entity property name (e.g. "addressComputed").
    /// </summary>
    protected virtual SortByRequest[] PreprocessSortBy(SortByRequest[] sortBy) => sortBy;

    /// <summary>
    /// Hook to post-process the projected page in memory before it is returned. Default is identity.
    /// Override when a response field can only be computed in C# (not in SQL) — e.g. overlaying values
    /// from a provider/law map onto the projected rows. Runs only on the current page of items.
    /// </summary>
    protected virtual List<TResponse> PostProcessItems(List<TResponse> items) => items;

    /// <summary>
    /// Async variant of <see cref="PostProcessItems"/>, run on the current page before it is returned. Default
    /// delegates to the synchronous hook. Override when the post-processing needs an awaitable call — e.g.
    /// overlaying values resolved from another module through a Kernel query.
    /// </summary>
    protected virtual Task<List<TResponse>> PostProcessItemsAsync(List<TResponse> items, CancellationToken ct) => Task.FromResult(PostProcessItems(items));

    public override void Configure()
    {
        Post($"/{EntityName.Kebaberize()}/{EndpointPath}");
        Roles(AllowedRoles());

        var entityName = typeof(TEntity).Name;
        Summary(s =>
        {
            s.Summary = $"Get filtered and paginated {entityName} list";
            s.Description = $"Retrieves a filtered, paginated and sorted list of {entityName}";

            s.Response<BaseGridResponse<TResponse>>(200, "Success");
            s.Response(400, "Bad request");
        });
    }

    public override async Task HandleAsync(BaseFilterSortPaginateRequest<TFilter> req, CancellationToken ct)
    {
        if (req.ItemsPerPage < 1)
        {
            AddError(r => r.ItemsPerPage, "Must be at least 1.");
            await Send.ErrorsAsync(400, ct);
            return;
        }

        if (req.ItemsPerPage > MaxItemsPerPage)
        {
            AddError(r => r.ItemsPerPage, $"Must not exceed {MaxItemsPerPage}.");
            await Send.ErrorsAsync(400, ct);
            return;
        }

        if (req.Page < 1)
        {
            AddError(r => r.Page, "Must be at least 1.");
            await Send.ErrorsAsync(400, ct);
            return;
        }

        var query = dbContext.Set<TEntity>().AsNoTracking();

        query = await ApplyUserScoping(query, User.GetId(), ct);

        query = ApplyBaseFiltering(query);

        if (req is { UseFilter: true, Filter: not null })
            query = ApplyCustomFiltering(query, req.Filter);

        var response = await query.GetGridDataAsync(
            PreprocessSortBy(req.SortBy),
            req.ItemsPerPage,
            req.Page,
            TResponse.Projection,
            ct);

        response.Items = await PostProcessItemsAsync(response.Items, ct);

        await Send.OkAsync(response, ct);
    }

    protected abstract IQueryable<TEntity> ApplyCustomFiltering(IQueryable<TEntity> query, TFilter filter);
}