using FastEndpoints;
using Humanizer;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.dto.request.@base.table;
using Sydowwe.Framework.application.dto.request.generic;
using Sydowwe.Framework.application.dto.request.@interface;
using Sydowwe.Framework.application.dto.response;
using Sydowwe.Framework.application.dto.response.@base;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.domain.entity.user;
using Sydowwe.Framework.domain.entityInterface;
using Sydowwe.Framework.domain.helper;
using Sydowwe.Framework.infrastructure.persistence;

namespace Sydowwe.Framework.application.endpoint.@base.read.pageFilterSort;

public abstract class BaseFilterSortEndpoint<TEntity, TResponse, TFilter>(DbContext dbContext)
    : Endpoint<BaseFilterSortRequest<TFilter>, List<TResponse>>
    where TEntity : class, IEntityWithUser, IEntityWithId
    where TResponse : class, IIdResponse, IProjectionResponse<TResponse, TEntity>
    where TFilter : class, IFilterRequest
{
    protected virtual string[] AllowedRoles() => this.GetDefaultRoles();

    public virtual string EndpointPath => "filter-sort";

    /// <summary>
    /// No-op by default. Override to add a WHERE clause that restricts results to the
    /// current user (e.g. by UserId or EmployeeId) — required whenever non-Admin roles
    /// can reach the endpoint, otherwise all rows leak.
    /// </summary>
    protected virtual Task<IQueryable<TEntity>> ApplyUserScoping(IQueryable<TEntity> query, long userId, CancellationToken ct = default) => Task.FromResult(query);

    public virtual SortByRequest[] AlwaysSortBy => [];

    protected virtual SortByRequest[] PreprocessSortBy(SortByRequest[] sortBy) => sortBy;

    public override void Configure()
    {
        var entityName = typeof(TEntity).Name;
        Post($"/{entityName.Kebaberize()}/{EndpointPath}");
        Roles(AllowedRoles());

        Summary(s =>
        {
            s.Summary = $"Get filtered and sorted {entityName} list";
            s.Description = $"Retrieves a filtered and sorted list of {entityName}";

            s.Response<List<TResponse>>(200, "Success");
            s.Response(400, "Bad request");
        });
    }

    public override async Task HandleAsync(BaseFilterSortRequest<TFilter> req, CancellationToken ct)
    {
        var query = dbContext.Set<TEntity>().AsNoTracking();

        query = await ApplyUserScoping(query, User.GetId());

        if (req is { UseFilter: true, Filter: not null })
            query = ApplyCustomFiltering(query, req.Filter);

        var sortBy = PreprocessSortBy(AlwaysSortBy.Concat(req.SortBy).ToArray());

        var response = await TResponse.Projection(query).SortByMany(sortBy).ToListAsync(ct);

        await Send.OkAsync(response, ct);
    }

    protected abstract IQueryable<TEntity> ApplyCustomFiltering(IQueryable<TEntity> query, TFilter filter);
}