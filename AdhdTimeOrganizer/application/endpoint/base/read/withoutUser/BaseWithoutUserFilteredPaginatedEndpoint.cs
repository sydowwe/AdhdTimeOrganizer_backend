using AdhdTimeOrganizer.application.dto.request.@base.table;
using AdhdTimeOrganizer.application.dto.request.@interface;
using AdhdTimeOrganizer.application.dto.response.@base;
using AdhdTimeOrganizer.application.mapper.@interface;
using AdhdTimeOrganizer.domain.helper;
using AdhdTimeOrganizer.domain.model.entityInterface;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Humanizer;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.@base.read.withoutUser;

public abstract class BaseWithoutUserFilteredPaginatedEndpoint<TEntity, TResponse, TFilter, TMapper>(
    AppCommandDbContext dbContext,
    TMapper mapper) : Endpoint<BaseFilterSortPaginateRequest<TFilter>, BaseTableResponse<TResponse>>
    where TEntity : class, IEntityWithId
    where TResponse : class, IIdResponse
    where TFilter : class, IFilterRequest
    where TMapper : class, IBaseReadMapper<TEntity, TResponse>
{
    public virtual string EndpointPath => "filtered-table";

    public virtual string[] AllowedRoles()
    {
        return EndpointHelper.GetUserOrHigherRoles();
    }


    public override void Configure()
    {
        var entityName = typeof(TEntity).Name;
        Post($"/{entityName.Kebaberize()}/{EndpointPath}");
        Summary(s =>
        {
            s.Summary = $"Get filtered and paginated {entityName} list";
            s.Description = $"Retrieves a filtered, paginated and sorted list of {entityName}";
            Roles(AllowedRoles());

            s.Response<BaseTableResponse<TResponse>>(200, "Success");
            s.Response(400, "Bad request");
        });
    }

    public override async Task HandleAsync(BaseFilterSortPaginateRequest<TFilter> req, CancellationToken ct)
    {
        try
        {
            var query = WithIncludes(dbContext.Set<TEntity>().AsNoTracking());

            if (req is { UseFilter: true, Filter: not null })
            {
                query = ApplyCustomFiltering(query, req.Filter);
            }

            var response = await query.GetTableDataAsync<TResponse, TEntity, TMapper>(
                req.SortBy,
                req.ItemsPerPage,
                req.Page,
                mapper,
                ct);

            await SendOkAsync(response, ct);
        }
        catch (Exception ex)
        {
            AddError($"An error occurred while retrieving filtered data: {ex.Message}");
            await SendErrorsAsync(500, ct);
        }
    }

    protected abstract IQueryable<TEntity> ApplyCustomFiltering(IQueryable<TEntity> query, TFilter filter);

    protected virtual IQueryable<TEntity> WithIncludes(IQueryable<TEntity> query)
    {
        return query;
    }
}