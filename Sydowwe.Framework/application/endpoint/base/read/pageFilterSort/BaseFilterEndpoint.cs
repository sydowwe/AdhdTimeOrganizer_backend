using FastEndpoints;
using Humanizer;
using Microsoft.EntityFrameworkCore;
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

public abstract class BaseFilterEndpoint<TEntity, TResponse, TFilter>(DbContext dbContext)
    : Endpoint<TFilter, List<TResponse>>
    where TEntity : class, IEntityWithUser, IEntityWithId
    where TResponse : class, IIdResponse, IProjectionResponse<TResponse, TEntity>
    where TFilter : class, IFilterRequest
{
    protected virtual string[] AllowedRoles() => this.GetDefaultRoles();

    public virtual string EndpointPath => "filter";

    /// <summary>
    /// No-op by default. Override to add a WHERE clause that restricts results to the
    /// current user (e.g. by UserId or EmployeeId) — required whenever non-Admin roles
    /// can reach the endpoint, otherwise all rows leak.
    /// </summary>
    protected virtual Task<IQueryable<TEntity>> ApplyUserScoping(IQueryable<TEntity> query, long userId, CancellationToken ct = default) => Task.FromResult(query);

    /// <summary>
    /// Standing sort order for this endpoint. Empty by default, which falls back to ordering by <c>Id</c>.
    /// Unlike the sort on <see cref="BaseSortEndpoint{TEntity,TResponse}"/>, this is developer-supplied
    /// rather than request-supplied, so it is applied to the <b>entity</b> query before projection — the
    /// keys must be entity property names (real columns), not response DTO members. A response member
    /// backed by a complex type (e.g. a time-of-day DTO) has no SQL ordering and would fail to translate.
    /// </summary>
    public virtual SortByRequest[] AlwaysSortBy => [];

    public override void Configure()
    {
        var entityName = typeof(TEntity).Name;
        Post($"/{entityName.Kebaberize()}/{EndpointPath}");
        Roles(AllowedRoles());

        Summary(s =>
        {
            s.Summary = $"Get filtered {entityName} list";
            s.Description = $"Retrieves a filtered list of {entityName}";

            s.Response<List<TResponse>>(200, "Success");
            s.Response(400, "Bad request");
        });
    }

    public override async Task HandleAsync(TFilter filter, CancellationToken ct)
    {
        var query = dbContext.Set<TEntity>().AsNoTracking();

        query = await ApplyUserScoping(query, User.GetId());

        query = ApplyCustomFiltering(query, filter);

        query = query.SortByMany(AlwaysSortBy);

        var response = await TResponse.Projection(query).ToListAsync(ct);

        await Send.OkAsync(response, ct);
    }

    protected abstract IQueryable<TEntity> ApplyCustomFiltering(IQueryable<TEntity> query, TFilter filter);
}